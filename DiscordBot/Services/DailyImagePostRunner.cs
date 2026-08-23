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
    private const int OptionsPerCategory = 3;

    // LLM에게 "매번 다르게 골라라"라고만 지시하면 결국 스스로 무작위성을 흉내내야 해서
    // 표정/눈동자색/옷차림처럼 눈에 띄는 항목이 특정 값으로 수렴하는 경향이 있었다.
    // 그래서 프로그램이 항목별 후보를 실제 난수로 뽑아 힌트로 제공하고, LLM은 그 후보를
    // 그대로 쓰거나 조합해 최종 값을 정하는 역할만 하도록 한다.
    private static readonly (string Label, string[] Options)[] RandomHintCategories =
    [
        ("표정", ["온화한 미소", "놀란 표정", "부끄러워 얼굴 붉힘", "자신만만한 미소", "졸린 표정", "진지한 표정",
            "활짝 웃는 표정", "새침하게 입을 삐죽인 표정", "결연한 눈빛", "장난스러운 윙크"]),
        ("종족/귀·꼬리", ["인간(귀 없음)", "고양이 귀와 꼬리", "여우 귀와 꼬리", "늑대 귀와 꼬리", "엘프 귀", "드래곤 뿔과 날개",
            "토끼 귀", "악마 뿔과 꼬리", "천사 날개"]),
        ("눈동자색", ["에메랄드 그린", "사파이어 블루", "호박색(amber)", "보라색", "오드아이(주황/파랑)", "진홍색",
            "은색", "황금색"]),
        ("헤어 컬러/스타일", ["은발 트윈테일", "분홍색 단발", "검은색 긴 생머리", "파란색 포니테일", "붉은 곱슬머리",
            "흰색 땋은 머리", "초록색 웨이브 머리", "보라색 언더컷"]),
        ("의상", ["후드 달린 로브", "판금 갑옷", "전통 기모노", "캐주얼 후드티와 청바지", "고딕 로리타 드레스",
            "군복 스타일 유니폼", "마녀 모자와 망토", "치파오", "세일러 교복", "미래풍 바디슈트"]),
        ("포즈", ["팔짱을 낀 포즈", "바닥에 앉은 포즈", "벽에 기댄 포즈", "공중에서 점프하는 포즈", "누워서 시청자를 보는 포즈",
            "어깨 너머로 뒤돌아보는 포즈", "허리에 손을 얹은 포즈", "기지개를 켜는 포즈", "한쪽 무릎을 꿇은 포즈"]),
        ("배경/장소", ["신비로운 숲", "아늑한 카페 실내", "달빛 비치는 옥상", "벚꽃이 흩날리는 공원", "미래도시의 거리",
            "노을 지는 해변", "눈 덮인 산골 마을", "고서로 가득한 도서관"]),
        ("시간대/날씨", ["맑은 대낮", "노을 지는 저녁", "별이 빛나는 밤", "비 내리는 흐린 날", "안개 낀 새벽", "눈 내리는 겨울날"]),
        ("분위기/조명", ["따뜻한 골든아워 조명", "몽환적인 림 라이팅", "부드러운 파스텔 아침빛", "극적인 폭풍 조명",
            "차분한 푸른 황혼빛", "아늑한 촛불 조명"])
    ];

    // 직전 라운드에 보여준 후보를 기억해뒀다가 이번 라운드 추첨 풀에서 제외한다 - LLM이 후보를
    // 그대로 답습하기만 해도 최소한 "보여지는 후보 자체"는 매번 달라지도록 시스템이 강제하기 위함.
    // 재시작하면 초기화되지만(별도 영속화 없음), 연달아 여러 번 게시하는 상황의 반복을 막는 것이 목적이라 충분하다.
    private IReadOnlyDictionary<string, IReadOnlyList<string>> m_LastShownOptions =
        new Dictionary<string, IReadOnlyList<string>>();

    public async Task<DailyImagePostResult> RunOnceAsync(bool force, CancellationToken cancellationToken)
    {
        var currentSettings = await settings.GetAsync(cancellationToken);
        if (!force && !currentSettings.Enabled)
        {
            logger.LogInformation("Daily image post skipped: feature is disabled.");
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

        GeneratedImage image;
        try
        {
            image = await imageGenerationClient.GenerateAsync(concept.PositivePrompt, null, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Daily image post failed: image generation threw.");
            return DailyImagePostResult.Skipped("image_generation_failed");
        }

        await using var stream = new MemoryStream(image.Bytes, writable: false);
        var caption = "🖼️ 오늘의 이미지\n" + concept.Caption;
        bool sent;
        try
        {
            sent = await channelSender.SendFileAsync(channelId, stream, image.FileName, caption, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Daily image post failed: sending the file to channel {ChannelId} threw.", channelId);
            return DailyImagePostResult.Skipped("send_failed");
        }

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
            var randomHints = BuildRandomHintBlock(n => Random.Shared.Next(n), recentlyShown: m_LastShownOptions);
            m_LastShownOptions = randomHints.PickedByCategory;
            var response = await chatClient.GenerateAsync(
                BuildConceptUserMessage(themePrompt, randomHints.Block), options, systemPrompt, cancellationToken);

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
        // 페르소나가 바뀌어도 말투 기본값(존댓말 등)은 일반 채팅과 동일하게 자동으로 따라가도록,
        // 채팅 응답에 쓰는 것과 같은 기본 방침을 그대로 재사용한다.
        return persona
            + "\n\n" + OllamaChatHistory.GetDefaultBehaviorInstruction()
            + "\n\n너는 매일 정해진 시각에 스스로 오늘 그릴 이미지의 컨셉을 정하고, ComfyUI용 프롬프트와"
            + " 그 컨셉을 소개하는 짧은 코멘트를 함께 작성한다."
            + " 사용자 메시지에는 표정/종족·귀꼬리/눈동자색/헤어/의상/포즈/배경/시간대·날씨/분위기·조명 항목별로"
            + " 프로그램이 방금 무작위로 뽑은 후보 목록이 함께 주어진다."
            + " 너는 그 후보 중 하나를 그대로 쓰거나, 여러 후보의 요소를 섞어 새로운 조합을 만들어도 된다 -"
            + " 중요한 것은 네가 직접 '무작위스러운' 값을 떠올리려 애쓰는 게 아니라, 주어진 후보를 실제로 활용해"
            + " 매번 다른 결과를 만드는 것이다. 후보를 무시하고 이전과 비슷한 무난한 값(예: 미소, 엘프 귀,"
            + " 에메랄드색 눈, 갑옷, 평범한 정면 포즈)으로 되돌아가지 마라."
            + " 후보 목록은 참고용 힌트일 뿐 강제 선택지는 아니므로, 후보보다 더 어울리는 아이디어가 떠오르면"
            + " 후보에 없는 값을 자유롭게 만들어도 된다 - 다만 그 경우에도 반드시 방금 나열한 무난한 기본값이나"
            + " 직전 컨셉과는 다른, 새로운 시도여야 한다."
            + " 사용자가 제공한 테마 힌트가 있다면 느슨하게만 참고하고 반드시 그대로 반영하지 않아도 된다.\n\n"
            + promptGenerationSystem
            + "\n위 [positive_prompt에 기본 포함할 권장 태그]는 화질/스타일 기준(예: 조명·렌더링 품질 관련 태그)으로만 참고하고,"
            + " 그 안에 포함된 구체적인 머리색·헤어스타일·종족/귀·꼬리·포즈·의상 묘사는 절대 그대로 가져오지 마라 -"
            + " 이번 컨셉에서 네가 새로 정한 값으로 완전히 대체해야 한다."
            + "\n출력 형식: {\"positive_prompt\":\"...\",\"caption\":\"...\"}"
            + " (다른 텍스트, 마크다운, 설명 없이 이 JSON 객체 하나만 출력)"
            + " caption은 방금 그린 이미지를 소개하는 1~2문장의 자연스러운 한국어 코멘트로,"
            + " 프롬프트 태그를 나열하지 말고 사람에게 말하듯 작성하라.";
    }

    private static string BuildConceptUserMessage(string themePrompt, string randomHintBlock)
    {
        var themeLine = string.IsNullOrWhiteSpace(themePrompt)
            ? "오늘의 테마 힌트는 없다. 완전히 자유롭게 오늘의 컨셉을 정하라."
            : $"오늘의 테마 힌트(참고만 하고 반드시 그대로 따르지 않아도 됨): {themePrompt}";

        return themeLine + "\n\n[오늘의 무작위 후보 - 프로그램이 방금 무작위로 뽑았다]\n" + randomHintBlock;
    }

    internal sealed record RandomHintResult(string Block, IReadOnlyDictionary<string, IReadOnlyList<string>> PickedByCategory);

    // internal: 결정론적 테스트를 위해 난수 소스를 파라미터로 주입받는다 (ComfyUIClient.RandomizeSeeds와 동일한 방식).
    // recentlyShown에 들어있는 값은 이번 추첨 풀에서 제외해, 직전 라운드와 겹치지 않는 후보를 우선 제시한다.
    internal static RandomHintResult BuildRandomHintBlock(
        Func<int, int> nextIndex,
        int optionsPerCategory = OptionsPerCategory,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? recentlyShown = null)
    {
        var pickedByCategory = new Dictionary<string, IReadOnlyList<string>>();
        var lines = new List<string>();
        foreach (var category in RandomHintCategories)
        {
            var exclude = recentlyShown != null && recentlyShown.TryGetValue(category.Label, out var previous)
                ? previous
                : [];
            var picked = PickRandomOptionsExcluding(category.Options, exclude, optionsPerCategory, nextIndex);
            pickedByCategory[category.Label] = picked;
            lines.Add($"- {category.Label}: {string.Join(", ", picked)}");
        }

        return new RandomHintResult(string.Join("\n", lines), pickedByCategory);
    }

    // exclude를 제외하고도 count만큼 남아있으면 그 안에서만 뽑고, 부족하면(항목 수가 작은 카테고리 등)
    // 전체 풀로 되돌아가 항상 count개를 채운다.
    internal static IReadOnlyList<string> PickRandomOptionsExcluding(
        IReadOnlyList<string> pool, IReadOnlyCollection<string> exclude, int count, Func<int, int> nextIndex)
    {
        var remaining = pool.Where(option => !exclude.Contains(option)).ToArray();
        var effectivePool = remaining.Length >= count ? remaining : pool;
        return PickRandomOptions(effectivePool, count, nextIndex);
    }

    internal static IReadOnlyList<string> PickRandomOptions(
        IReadOnlyList<string> pool, int count, Func<int, int> nextIndex)
    {
        var items = pool.ToArray();
        var take = Math.Min(count, items.Length);
        for (var i = 0; i < take; i++)
        {
            var j = i + nextIndex(items.Length - i);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items[..take];
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
