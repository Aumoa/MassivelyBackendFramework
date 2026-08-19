using System.Text.Json;
using System.Text.Json.Serialization;
using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Services.ImageGeneration;

namespace DiscordBot.Services;

internal class DiscordImageTools(
    SocketMessage message,
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings,
    IImageGenerationClient imageGenerationClient,
    ImagePromptProfileProvider promptProfileProvider,
    ILogger<DiscordImageTools> logger) : IToolFunctionDescriptionProvider
{
    private const string GenerateImageDescription =
        "사용자의 이미지 생성, 그림 생성, 일러스트 생성, 이미지 수정 요청을 처리합니다. " +
        "user_request에는 사용자의 요청 원문과 필요한 대화 맥락을 자연어로 전달하세요. " +
        "프롬프트 작성은 도구 내부에서 별도로 처리합니다.";

    [ToolFunction(
        Name = "generate_image",
        Description = GenerateImageDescription)]
    public async Task<string> GenerateImageAsync(
        [ToolParameterInfo(Name = "user_request", Description = "사용자의 이미지 요청 원문과 필요한 대화 맥락입니다. 후속 수정 요청이면 이전 이미지에서 유지할 내용과 바꿀 내용을 함께 적으세요.")]
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        IUserMessage? statusMessage = null;
        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? statusUpdateTask = null;
        ImageGenerationProgress? latestProgress = null;
        object latestProgressLock = new();

        try
        {
            statusMessage = await message.Channel.SendMessageAsync("이미지 프롬프트를 준비 중입니다...");
            var promptDraft = await GeneratePromptDraftAsync(userRequest, cancellationToken);
            await statusMessage.ModifyAsync(p => p.Content = "이미지를 생성 중입니다...");

            statusUpdateTask = UpdateStatusMessageAsync(
                statusMessage,
                () =>
                {
                    lock (latestProgressLock)
                    {
                        return latestProgress;
                    }
                },
                statusCts.Token);

            var progress = new Progress<ImageGenerationProgress>(value =>
            {
                lock (latestProgressLock)
                {
                    latestProgress = value;
                }
            });

            var image = await imageGenerationClient.GenerateAsync(
                promptDraft.PositivePrompt,
                promptDraft.NegativePrompt,
                progress,
                cancellationToken);

            await StopStatusUpdateAsync(statusCts, statusUpdateTask);

            await using var stream = new MemoryStream(image.Bytes, writable: false);
            await message.Channel.SendFileAsync(stream, image.FileName);
            await DeleteStatusMessageAsync(statusMessage);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                message = "이미지를 생성해서 Discord 채널에 업로드했습니다.",
                file_name = image.FileName,
                content_type = image.ContentType,
                positive_prompt = promptDraft.PositivePrompt,
                negative_prompt = promptDraft.NegativePrompt
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopStatusUpdateAsync(statusCts, statusUpdateTask);
            if (statusMessage != null)
            {
                await statusMessage.ModifyAsync(p => p.Content = "이미지 생성 시간이 초과되었습니다.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "error",
                reason = "timeout",
                message = "이미지 생성 시간이 초과되었습니다."
            });
        }
        catch (Exception e)
        {
            await StopStatusUpdateAsync(statusCts, statusUpdateTask);
            if (statusMessage != null)
            {
                await statusMessage.ModifyAsync(p => p.Content = "이미지 생성에 실패했습니다.");
            }

            logger.LogError(e, "Failed to generate image.");
            return JsonSerializer.Serialize(new
            {
                status = "error",
                reason = "image_generation_failed",
                message = e.Message
            });
        }
    }

    private async Task UpdateStatusMessageAsync(
        IUserMessage statusMessage,
        Func<ImageGenerationProgress?> getProgress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        string[] frames = ["이미지를 생성 중입니다.", "이미지를 생성 중입니다..", "이미지를 생성 중입니다..."];
        int frameIndex = 0;

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                var frame = frames[frameIndex++ % frames.Length];
                var progress = getProgress();
                await statusMessage.ModifyAsync(p => p.Content = BuildStatusMessage(frame, elapsed, progress));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to update image generation status message.");
        }
    }

    private static string BuildStatusMessage(
        string frame,
        TimeSpan elapsed,
        ImageGenerationProgress? progress)
    {
        List<string> lines = [frame];
        if (progress != null)
        {
            if (!string.IsNullOrWhiteSpace(progress.NodeTitle))
            {
                lines.Add($"현재 단계: {progress.NodeTitle}");
            }

            if (progress.Value.HasValue && progress.Max is > 0)
            {
                var percent = Math.Clamp(progress.Value.Value / (double)progress.Max.Value * 100, 0, 100);
                lines.Add($"진행률: {progress.Value} / {progress.Max} ({percent:F0}%)");
            }
            else if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                lines.Add($"상태: {progress.Status}");
            }
        }

        lines.Add($"경과 시간: {elapsed:mm\\:ss}");
        return string.Join("\n", lines);
    }

    private static async Task StopStatusUpdateAsync(CancellationTokenSource cts, Task? statusUpdateTask)
    {
        await cts.CancelAsync();
        if (statusUpdateTask == null)
        {
            return;
        }

        try
        {
            await statusUpdateTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DeleteStatusMessageAsync(IUserMessage? statusMessage)
    {
        if (statusMessage == null)
        {
            return;
        }

        try
        {
            await statusMessage.DeleteAsync();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to delete image generation status message.");
        }
    }

    public string? GetToolFunctionDescription(string functionName)
    {
        return functionName == "generate_image"
            ? GenerateImageDescription
            : null;
    }

    private async Task<ImagePromptDraft> GeneratePromptDraftAsync(string userRequest, CancellationToken cancellationToken)
    {
        var normalizedRequest = string.IsNullOrWhiteSpace(userRequest)
            ? message.Content
            : userRequest.Trim();

        var fallback = await BuildFallbackPromptDraftAsync(normalizedRequest, cancellationToken);
        try
        {
            var settings = await claudeSettings.GetAsync(cancellationToken);
            var options = new ChatCompletionOptions
            {
                Model = settings.SummaryModel,
                Temperature = 0.4f,
                MaxTokens = Math.Clamp(settings.DefaultMaxTokens, 256, 2048),
                ContextLength = 8192
            };

            var systemPrompt = await promptProfileProvider.BuildPromptGenerationSystemAsync(cancellationToken);
            var response = await chatClient.GenerateAsync(
                BuildPromptGenerationUserMessage(normalizedRequest),
                options,
                systemPrompt,
                cancellationToken);

            if (TryParsePromptDraft(response, out var promptDraft))
            {
                return ApplyFixedTags(NormalizePromptDraft(promptDraft, fallback));
            }

            logger.LogWarning("Failed to parse image prompt draft. Response: {Response}", response);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to generate image prompt draft. Falling back to direct request prompt.");
        }

        return ApplyFixedTags(fallback);
    }

    private ImagePromptDraft ApplyFixedTags(ImagePromptDraft promptDraft)
    {
        var (positivePrompt, negativePrompt) = promptProfileProvider.ApplyFixedTags(
            promptDraft.PositivePrompt,
            promptDraft.NegativePrompt);
        return new ImagePromptDraft(positivePrompt, negativePrompt);
    }

    private static string BuildPromptGenerationUserMessage(string userRequest)
    {
        return "다음 사용자 요청을 이미지 생성 프롬프트로 변환하세요.\n\n[사용자 요청]\n" + userRequest;
    }

    private async Task<ImagePromptDraft> BuildFallbackPromptDraftAsync(string userRequest, CancellationToken cancellationToken)
    {
        var (positivePrompt, negativePrompt) = await promptProfileProvider.BuildFallbackPromptsAsync(userRequest, cancellationToken);
        return new ImagePromptDraft(positivePrompt, negativePrompt);
    }

    private static ImagePromptDraft NormalizePromptDraft(ImagePromptDraft promptDraft, ImagePromptDraft fallback)
    {
        var positivePrompt = string.IsNullOrWhiteSpace(promptDraft.PositivePrompt)
            ? fallback.PositivePrompt
            : promptDraft.PositivePrompt.Trim();
        var negativePrompt = string.IsNullOrWhiteSpace(promptDraft.NegativePrompt)
            ? fallback.NegativePrompt
            : promptDraft.NegativePrompt.Trim();

        return new ImagePromptDraft(positivePrompt, negativePrompt);
    }

    private static bool TryParsePromptDraft(string response, out ImagePromptDraft promptDraft)
    {
        promptDraft = default!;
        var json = ExtractJsonObject(response);
        if (json == null)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ImagePromptDraftDto>(json);
            if (parsed == null)
            {
                return false;
            }

            promptDraft = new ImagePromptDraft(
                parsed.PositivePrompt ?? string.Empty,
                parsed.NegativePrompt ?? string.Empty);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractJsonObject(string response)
    {
        var trimmed = response.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start
            ? trimmed[start..(end + 1)]
            : null;
    }

    private sealed record ImagePromptDraft(string PositivePrompt, string NegativePrompt);

    private sealed record ImagePromptDraftDto(
        [property: JsonPropertyName("positive_prompt")] string? PositivePrompt,
        [property: JsonPropertyName("negative_prompt")] string? NegativePrompt);
}
