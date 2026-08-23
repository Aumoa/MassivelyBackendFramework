using System.Text.Json;
using System.Text.Json.Serialization;
using AI;
using DiscordBot.Services.ImageGeneration;

namespace DiscordBot.Services;

internal sealed record DailyImageConcept(string PositivePrompt, string Caption);

internal sealed record DailyImageConceptDto(
    [property: JsonPropertyName("positive_prompt")] string? PositivePrompt,
    [property: JsonPropertyName("caption")] string? Caption);

internal sealed record DailyImagePostResult(bool Succeeded, string Reason)
{
    public static DailyImagePostResult Success(string fileName) => new(true, fileName);

    public static DailyImagePostResult Skipped(string reason) => new(false, reason);
}

internal sealed class DailyImagePostRunner(
    IDailyImagePostSettingsService settings,
    IImageGenerationClient imageGenerationClient,
    ImagePromptProfileProvider promptProfileProvider,
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings,
    IDiscordChannelSender channelSender,
    ILogger<DailyImagePostRunner> logger)
{
    private const string FallbackCaption = "오늘의 이미지를 그려봤어요.";

    public async Task<DailyImagePostResult> RunOnceAsync(bool force, CancellationToken cancellationToken)
    {
        var currentSettings = await settings.GetAsync(cancellationToken);
        if (!force && !currentSettings.Enabled)
        {
            return DailyImagePostResult.Skipped("disabled");
        }

        if (!ulong.TryParse(currentSettings.ChannelId, out var channelId))
        {
            logger.LogWarning("Daily image post skipped: channel id is not configured or invalid.");
            return DailyImagePostResult.Skipped("invalid_channel");
        }

        if (!await channelSender.WaitForConnectionAsync(TimeSpan.FromMinutes(2), cancellationToken))
        {
            logger.LogWarning("Daily image post skipped: Discord client did not become ready in time.");
            return DailyImagePostResult.Skipped("not_connected");
        }

        var concept = await GenerateConceptAsync(currentSettings.ThemePrompt, cancellationToken);
        var image = await imageGenerationClient.GenerateAsync(concept.PositivePrompt, null, cancellationToken);

        await using var stream = new MemoryStream(image.Bytes, writable: false);
        var caption = "🖼️ 오늘의 이미지\n" + concept.Caption;
        var sent = await channelSender.SendFileAsync(channelId, stream, image.FileName, caption, cancellationToken);
        if (!sent)
        {
            logger.LogWarning("Daily image post failed: channel {ChannelId} not found.", channelId);
            return DailyImagePostResult.Skipped("channel_not_found");
        }

        logger.LogInformation("Daily image posted to channel {ChannelId}.", channelId);
        return DailyImagePostResult.Success(image.FileName);
    }

    // 이미지 프롬프트와 캡션을 한 번의 LLM 호출로 함께 생성 - 그림 내용과 코멘트가 서로 어긋나지 않게 한다.
    private async Task<DailyImageConcept> GenerateConceptAsync(string themePrompt, CancellationToken cancellationToken)
    {
        var fallback = await BuildFallbackConceptAsync(themePrompt, cancellationToken);
        try
        {
            var claudeSettingsData = await claudeSettings.GetAsync(cancellationToken);
            var options = new ChatCompletionOptions
            {
                Model = claudeSettingsData.SummaryModel,
                Temperature = 0.8f,
                MaxTokens = Math.Clamp(claudeSettingsData.DefaultMaxTokens, 256, 1024),
                ContextLength = 8192
            };

            var systemPrompt = await BuildConceptSystemPromptAsync(
                claudeSettingsData.Instructions ?? "", cancellationToken);
            var response = await chatClient.GenerateAsync(
                BuildConceptUserMessage(themePrompt), options, systemPrompt, cancellationToken);

            if (TryParseConcept(response, out var parsed))
            {
                return NormalizeConcept(parsed, fallback);
            }

            logger.LogWarning("Failed to parse daily image concept. Response: {Response}", response);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to generate daily image concept. Falling back to a static prompt/caption.");
        }

        return fallback;
    }

    private async Task<DailyImageConcept> BuildFallbackConceptAsync(string themePrompt, CancellationToken cancellationToken)
    {
        var positive = await promptProfileProvider.BuildFallbackPromptsAsync(themePrompt, cancellationToken);
        var finalPositive = promptProfileProvider.ApplyFixedTags(positive);
        return new DailyImageConcept(finalPositive, FallbackCaption);
    }

    private async Task<string> BuildConceptSystemPromptAsync(string persona, CancellationToken cancellationToken)
    {
        // 기존 수동 생성 도구가 쓰는 것과 같은 워크플로우 규칙/권장 태그를 재사용해 품질 기준을 맞춘다.
        var promptGenerationSystem = await promptProfileProvider.BuildPromptGenerationSystemAsync(cancellationToken);
        return persona
            + "\n\n너는 매일 정해진 시각에 스스로 오늘 그릴 이미지의 컨셉을 정하고, ComfyUI용 프롬프트와"
            + " 그 컨셉을 소개하는 짧은 코멘트를 함께 작성한다."
            + " 헤어컬러/헤어스타일, 포즈/표정, 의상, 배경/장소, 시간대/날씨, 분위기·조명 중에서"
            + " 매번 자유롭게, 최대한 다르게 골라 컨셉을 정하라 - 매일 같은 선택을 반복하지 마라."
            + " 사용자가 제공한 테마 힌트가 있다면 느슨하게만 참고하고 반드시 그대로 반영하지 않아도 된다.\n\n"
            + promptGenerationSystem
            + "\n출력 형식: {\"positive_prompt\":\"...\",\"caption\":\"...\"}"
            + " (다른 텍스트, 마크다운, 설명 없이 이 JSON 객체 하나만 출력)"
            + " caption은 방금 그린 이미지를 소개하는 1~2문장의 자연스러운 한국어 코멘트로,"
            + " 프롬프트 태그를 나열하지 말고 사람에게 말하듯 작성하라.";
    }

    private static string BuildConceptUserMessage(string themePrompt)
    {
        return string.IsNullOrWhiteSpace(themePrompt)
            ? "오늘의 테마 힌트는 없다. 완전히 자유롭게 오늘의 컨셉을 정하라."
            : $"오늘의 테마 힌트(참고만 하고 반드시 그대로 따르지 않아도 됨): {themePrompt}";
    }

    private DailyImageConcept NormalizeConcept(DailyImageConceptDto parsed, DailyImageConcept fallback)
    {
        var caption = string.IsNullOrWhiteSpace(parsed.Caption)
            ? fallback.Caption
            : Truncate(parsed.Caption.Trim(), 300);

        if (string.IsNullOrWhiteSpace(parsed.PositivePrompt))
        {
            return fallback with { Caption = caption };
        }

        var finalPositive = promptProfileProvider.ApplyFixedTags(parsed.PositivePrompt.Trim());
        return new DailyImageConcept(finalPositive, caption);
    }

    // DiscordImageTools.TryParsePromptDraft/ExtractJsonObject와 동일한 방식(응답에서 첫 { ~ 마지막 } 구간만 JSON으로 파싱).
    private static bool TryParseConcept(string response, out DailyImageConceptDto parsed)
    {
        parsed = default!;
        var trimmed = response.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            var result = JsonSerializer.Deserialize<DailyImageConceptDto>(trimmed[start..(end + 1)]);
            if (result == null)
            {
                return false;
            }

            parsed = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
