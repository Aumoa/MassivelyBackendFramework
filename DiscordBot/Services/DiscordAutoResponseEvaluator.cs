using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI;
using DiscordBot.Options;

namespace DiscordBot.Services;

internal sealed record DiscordAutoResponseMessage(
    string MessageId,
    string? GuildId,
    string ChannelId,
    string AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp,
    bool IsDirectMessage,
    bool MentionsBot,
    bool ReferencesBotMessage,
    bool HasAttachments);

internal sealed record DiscordAutoResponseDecision(
    bool ShouldRespond,
    string Reason,
    string Focus);

internal interface IDiscordAutoResponseEvaluator
{
    ValueTask<DiscordAutoResponseDecision> EvaluateAsync(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        CancellationToken cancellationToken = default);
}

internal sealed partial class DiscordAutoResponseEvaluator(
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings,
    IAutoResponseSettingsService autoResponseSettings,
    ILogger<DiscordAutoResponseEvaluator> logger) : IDiscordAutoResponseEvaluator
{
    private const int DefaultMaxBufferedMessages = 20;
    private const int MaxBufferedMessagesLimit = 50;
    private const int DefaultClassifierMaxTokens = 160;
    private const int MaxClassifierMaxTokensLimit = 512;
    private const int MaxClassifierRequestAttempts = 3;
    private const int InitialClassifierRetryDelayMilliseconds = 250;

    private static readonly string[] QuestionMarkers =
    [
        "?",
        "？",
        "나요",
        "까요",
        "뭐",
        "무엇",
        "누가",
        "어디",
        "언제",
        "왜",
        "어떻게",
        "어때",
        "어떰",
        "알아",
        "아는 사람",
        "도와",
        "추천",
        "답변",
        "의견"
    ];

    public async ValueTask<DiscordAutoResponseDecision> EvaluateAsync(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var currentOptions = (await autoResponseSettings.GetAsync(cancellationToken)).ToOptions();
        if (!currentOptions.Enabled || messages.Count == 0)
        {
            return Decline("disabled_or_empty");
        }

        var normalizedMessages = TakeEvaluationWindow(messages, currentOptions);
        if (!normalizedMessages.Any(message => PassesStaticFilter(message, currentOptions)))
        {
            return Decline("static_filter_not_matched");
        }

        var settings = await claudeSettings.GetAsync(cancellationToken);
        var model = string.IsNullOrWhiteSpace(currentOptions.ClassifierModel)
            ? settings.SummaryModel
            : currentOptions.ClassifierModel.Trim();
        var maxTokens = NormalizeRange(
            currentOptions.ClassifierMaxTokens,
            DefaultClassifierMaxTokens,
            MaxClassifierMaxTokensLimit);

        var prompt = BuildClassificationPrompt(normalizedMessages, currentOptions);
        var completionOptions = new ChatCompletionOptions
        {
            Model = model,
            Temperature = 0,
            MaxTokens = maxTokens,
            ContextLength = 4096
        };
        var response = await ExecuteClassifierRequestAsync(
            operation: token => chatClient.GenerateAsync(
                prompt,
                completionOptions,
                BuildClassificationSystemPrompt(),
                token),
            onRetry: (exception, attempt, delay) => logger.LogWarning(
                exception,
                "Transient auto response classifier request failure on attempt {Attempt}; retrying after {DelayMilliseconds} ms.",
                attempt,
                delay.TotalMilliseconds),
            cancellationToken: cancellationToken);

        var decision = ParseDecision(response);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Auto response classifier decision: {ShouldRespond}, {Reason}, {Focus}",
                decision.ShouldRespond,
                decision.Reason,
                decision.Focus);
        }

        return decision;
    }

    internal static async Task<T> ExecuteClassifierRequestAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Action<HttpRequestException, int, TimeSpan>? onRetry,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        delayAsync ??= static (delay, token) => Task.Delay(delay, token);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (HttpRequestException exception) when (
                attempt < MaxClassifierRequestAttempts
                && IsTransientClassifierFailure(exception))
            {
                var delay = TimeSpan.FromMilliseconds(
                    InitialClassifierRetryDelayMilliseconds * (1 << (attempt - 1)));
                onRetry?.Invoke(exception, attempt, delay);
                await delayAsync(delay, cancellationToken);
            }
        }
    }

    internal static bool IsTransientClassifierFailure(HttpRequestException exception)
    {
        if (exception.StatusCode == null)
        {
            return true;
        }

        var statusCode = exception.StatusCode.Value;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
               || (int)statusCode >= 500;
    }

    internal static bool PassesStaticFilter(
        DiscordAutoResponseMessage message,
        AutoResponseOptions options)
    {
        if (message.IsDirectMessage || message.MentionsBot)
        {
            return false;
        }

        var content = NormalizeContent(message.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (IsUnsupportedBareReference(content, message.HasAttachments))
        {
            return false;
        }

        return message.ReferencesBotMessage
               || ContainsBotAlias(content, options.BotNameAliases)
               || LooksQuestionLike(content);
    }

    internal static DiscordAutoResponseDecision ParseDecision(string response)
    {
        var json = ExtractJsonObject(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Decline("invalid_classifier_response");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var shouldRespond = ReadBoolean(root, "should_respond")
                                ?? ReadBoolean(root, "shouldRespond")
                                ?? false;
            var reason = ReadString(root, "reason") ?? string.Empty;
            var focus = ReadString(root, "focus")
                        ?? ReadString(root, "response_focus")
                        ?? string.Empty;

            return new DiscordAutoResponseDecision(
                shouldRespond,
                NormalizeDecisionText(reason, shouldRespond ? "classifier_accepted" : "classifier_declined"),
                NormalizeDecisionText(focus, string.Empty));
        }
        catch (JsonException)
        {
            return Decline("invalid_classifier_response");
        }
    }

    internal static string BuildAutomaticResponsePrompt(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        DiscordAutoResponseDecision decision)
    {
        var normalizedMessages = messages.Count == 0
            ? []
            : messages
                .OrderBy(message => message.Timestamp)
                .ToArray();
        var focus = string.IsNullOrWhiteSpace(decision.Focus)
            ? "대화 흐름상 자연스럽게 반응할 만한 지점"
            : decision.Focus.Trim();

        return $"""
[자동 응답 모드]
아래는 현재 Discord 채널에서 자동 응답 후보로 모인 최근 대화입니다.
봇은 멘션되지 않았지만 대화 흐름상 짧게 끼어들 수 있습니다.
너무 길게 설명하지 말고, 실제 사람이 근처 대화에 자연스럽게 반응하듯 1~3문장으로 답하세요.
확신이 낮거나 필요한 정보가 현재 텍스트만으로 부족하면 억지로 답하지 말고 짧게 확인 질문을 하세요.
과거 채팅, 첨부, 이미지, 답장 맥락이 필요해 보이면 현재 채널의 채팅 조회 도구를 먼저 사용하세요.

판정 이유: {decision.Reason}
응답 초점: {focus}

[최근 대화]
{BuildMessageList(normalizedMessages)}
""";
    }

    internal static string BuildClassificationPrompt(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        AutoResponseOptions options)
    {
        var normalizedMessages = messages
            .OrderBy(message => message.Timestamp)
            .ToArray();
        var aliases = options.BotNameAliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        const string OutputFormat = """{"should_respond":false,"reason":"짧은 이유","focus":"응답한다면 초점을 둘 메시지나 주제"}""";

        return $"""
봇 이름/지칭 후보: {string.Join(", ", aliases)}

아래 Discord 채널 대화 묶음이 자동 응답할 만한지 판정하세요.
반드시 JSON 객체 하나만 출력하세요.

출력 형식:
{OutputFormat}

[대화 묶음]
{BuildMessageList(normalizedMessages)}
""";
    }

    private static string BuildClassificationSystemPrompt()
    {
        return """
너는 Discord 자동 응답 트리거 판정기입니다.
비용 절약과 대화 방해 최소화가 최우선입니다.

should_respond=true 조건:
- 봇 이름, AI, 봇/인공지능 지칭이 태그 없이 언급되어 봇의 짧은 반응이 자연스러운 경우.
- 누군가 질문이나 도움 요청을 했고, 같은 묶음 안에서 사람이 충분히 답하지 않은 경우.
- 봇의 이전 응답에 대한 후속 반응처럼 보이는 경우.

should_respond=false 조건:
- 1:1 DM, 봇 태그/멘션, 이미 사람이 답한 대화, 잡담/감탄/밈처럼 끼어들 필요가 낮은 경우.
- 유튜브/웹 링크, 이미지, 첨부만 던지고 "이거 어때?"처럼 AI가 내용을 볼 수 없어 판단이 어려운 경우.
- 대화에 끼어드는 것이 어색하거나 비용 대비 가치가 낮은 경우.

JSON 이외의 문장을 출력하지 마세요.
""";
    }

    private static IReadOnlyList<DiscordAutoResponseMessage> TakeEvaluationWindow(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        AutoResponseOptions options)
    {
        var maxMessages = NormalizeRange(
            options.MaxBufferedMessages,
            DefaultMaxBufferedMessages,
            MaxBufferedMessagesLimit);
        return messages
            .OrderBy(message => message.Timestamp)
            .TakeLast(maxMessages)
            .ToArray();
    }

    private static bool ContainsBotAlias(string content, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var normalizedAlias = alias.Trim();
            if (normalizedAlias.All(static c => c < 128 && char.IsLetterOrDigit(c)))
            {
                var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(normalizedAlias)}(?![A-Za-z0-9])";
                if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    return true;
                }

                continue;
            }

            if (content.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksQuestionLike(string content)
    {
        return QuestionMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnsupportedBareReference(string content, bool hasAttachments)
    {
        var withoutUrls = UrlRegex().Replace(content, string.Empty).Trim();
        var hadUrl = withoutUrls.Length != content.Trim().Length;
        if (!hadUrl && !hasAttachments)
        {
            return false;
        }

        if (withoutUrls.Length > 16)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(withoutUrls)
               || VagueReferenceRegex().IsMatch(withoutUrls);
    }

    private static string BuildMessageList(IReadOnlyList<DiscordAutoResponseMessage> messages)
    {
        if (messages.Count == 0)
        {
            return "(메시지 없음)";
        }

        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            var flags = new List<string>();
            if (message.ReferencesBotMessage)
            {
                flags.Add("references_bot");
            }

            if (message.HasAttachments)
            {
                flags.Add("has_attachment");
            }

            var flagText = flags.Count == 0 ? "" : $" flags={string.Join(",", flags)}";
            sb.AppendLine(
                $"- [{message.Timestamp:O}] message_id={message.MessageId} author={message.AuthorName}({message.AuthorId}){flagText}: {NormalizeContent(message.Content)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string NormalizeContent(string content)
    {
        return (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static string? ExtractJsonObject(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        return response[start..(end + 1)];
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DiscordAutoResponseDecision Decline(string reason)
    {
        return new DiscordAutoResponseDecision(false, reason, string.Empty);
    }

    private static string NormalizeDecisionText(string value, string fallback)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        const int MaxLength = 400;
        return normalized.Length <= MaxLength ? normalized : normalized[..MaxLength];
    }

    private static int NormalizeRange(int value, int fallback, int max)
    {
        if (value <= 0)
        {
            value = fallback;
        }

        return Math.Clamp(value, 1, max);
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?:(?:이거|저거|그거|이건|저건|그건)\s*)?(?:어때|어떰|봐줘|보고\s*말해줘|어떻게\s*생각해|what\s+do\s+you\s+think)\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VagueReferenceRegex();
}
