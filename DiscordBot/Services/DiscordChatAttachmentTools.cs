using System.Globalization;
using System.Text.RegularExpressions;
using AI;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal partial class DiscordChatAttachmentTools(
    SocketMessage message,
    IChatAttachmentRepository chatAttachmentRepository,
    ILogger<DiscordChatAttachmentTools> logger)
{
    private const int DefaultMaxCharacters = 24_000;
    private const int MaxCharactersLimit = 60_000;

    [ToolFunction(
        Name = "load_chat_attachment",
        Description = @"현재 메시지에 직접 첨부되지 않은 과거 채팅 문서가 필요할 때 호출합니다.

사용자가 '아까 올린 pdf', '위 문서', '방금 업데이트 내역 파일', '그 txt', '답글 단 문서'처럼 과거 문서 attachment를 가리키며 요약, 비교, 질의응답을 요청할 때 사용하세요.

target 값:
- latest: 현재 채널에서 이 메시지보다 전에 올라온 가장 최근 문서 1개를 가져옵니다.
- replied: 사용자가 답글을 단 원본 메시지의 문서를 가져옵니다.
- message_id: message_id 파라미터로 지정한 메시지의 문서를 가져옵니다. Discord 메시지 URL 전체를 message_id에 넣어도 됩니다.

현재 사용자의 메시지에 문서가 직접 첨부되어 있다면 이미 AI 입력에 포함되어 있으므로 이 도구를 호출하지 마세요.")]
    public async Task<ToolExecutionResult> LoadChatAttachmentAsync(
        [ToolParameterInfo(Description = "문서를 찾는 방식입니다. latest, replied, message_id 중 하나를 사용하세요. 기본값은 latest입니다.")]
        string target = "latest",
        [ToolParameterInfo(Description = "target이 message_id일 때 사용할 Discord 메시지 ID 또는 메시지 URL입니다.")]
        string? message_id = null,
        [ToolParameterInfo(Description = "응답에 포함할 추출 텍스트 최대 글자 수입니다. 기본 24000, 최대 60000입니다.")]
        int max_characters = DefaultMaxCharacters,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target)
            ? "latest"
            : target.Trim().ToLowerInvariant();
        var channelId = message.Channel.Id.ToString();

        IReadOnlyList<ChatAttachmentData> attachments = normalizedTarget switch
        {
            "replied" => await LoadRepliedAttachmentsAsync(channelId, cancellationToken),
            "message_id" => await LoadMessageAttachmentsAsync(channelId, message_id, cancellationToken),
            _ => await LoadLatestAttachmentAsync(channelId, cancellationToken)
        };

        if (attachments.Count == 0)
        {
            return ToolExecutionResult.FromText("조건에 맞는 과거 채팅 문서를 찾지 못했습니다.");
        }

        logger.LogInformation(
            "Loaded {Count} chat attachments from target {Target} for tool call.",
            attachments.Count,
            normalizedTarget);

        max_characters = Math.Clamp(max_characters, 1_000, MaxCharactersLimit);
        return ToolExecutionResult.FromText(BuildAttachmentDetails(attachments, max_characters));
    }

    [ToolFunction(
        Name = "search_chat_attachments",
        Description = @"현재 채팅방에 저장된 문서 attachment의 추출 텍스트를 키워드로 검색합니다.

사용자가 과거에 올린 문서 중 특정 내용, 업데이트 항목, 정책, 오류 코드, 변경 내역을 찾아달라고 할 때 사용하세요.
검색 결과에는 원본 Discord 메시지 링크가 포함됩니다. 사용자 답변에 원본 링크가 필요하면 Link 값을 그대로 복사해 보여주세요.")]
    public async Task<string> SearchChatAttachmentsAsync(
        [ToolParameterInfo(Description = "검색할 키워드들을 콤마(,)로 구분한 문자열입니다. 각 키워드는 2글자 이상이어야 합니다.")]
        string keywords,
        [ToolParameterInfo(Description = "최대 결과 수입니다. 1~20, 기본 5입니다.")]
        int limit = 5,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간입니다. 지정된 timezone 기준입니다. 예: 2026-06-01T00:00:00")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간입니다. 지정된 timezone 기준입니다. 예: 2026-06-13T23:59:59")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID입니다. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        CancellationToken cancellationToken = default)
    {
        var validKeywords = (keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static keyword => keyword.Length >= 2)
            .ToList();
        if (validKeywords.Count == 0)
        {
            return "검색에 사용할 2글자 이상의 키워드가 필요합니다.";
        }

        var tz = ResolveTimeZone(timezone);
        if (!TryParseOptionalDateTime(from_date, tz, out var from, out var fromError))
        {
            return fromError;
        }

        if (!TryParseOptionalDateTime(to_date, tz, out var to, out var toError))
        {
            return toError;
        }

        limit = Math.Clamp(limit, 1, 20);
        var channelId = message.Channel.Id.ToString();
        var results = await chatAttachmentRepository.SearchAsync(
            channelId,
            validKeywords,
            limit,
            from,
            to,
            cancellationToken);

        if (results.Count == 0)
        {
            return "검색 결과가 없습니다.";
        }

        List<string> lines =
        [
            $"검색 키워드: {string.Join(", ", validKeywords)}",
            $"결과 {results.Count}건:",
            ""
        ];

        for (var i = 0; i < results.Count; i++)
        {
            var attachment = results[i];
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(attachment.CreatedAt), tz);
            lines.Add($"--- 결과 #{i + 1} ---");
            lines.Add($"ChatAttachmentId: {attachment.Id}");
            lines.Add($"ChatLogId: {attachment.ChatLogId}");
            lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"File: {attachment.FileName ?? "(unknown)"}");
            lines.Add($"Content-Type: {attachment.ContentType}");
            lines.Add($"Extraction-Status: {attachment.ExtractionStatus}");
            lines.Add($"Link: {BuildMessageReference(attachment)}");
            lines.Add($"Excerpt: {BuildExcerpt(attachment.ExtractedText, 1200)}");
            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadLatestAttachmentAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var latest = await chatAttachmentRepository.GetLatestAsync(channelId, message.Timestamp, cancellationToken);
        return latest == null ? [] : [latest];
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadRepliedAttachmentsAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var messageId = message.Reference?.MessageId;
        if (messageId == null)
        {
            return [];
        }

        return await chatAttachmentRepository.GetByMessageIdAsync(channelId, messageId.Value.ToString(), cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadMessageAttachmentsAsync(
        string channelId,
        string? messageIdOrUrl,
        CancellationToken cancellationToken)
    {
        var messageId = ExtractMessageId(messageIdOrUrl);
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return [];
        }

        return await chatAttachmentRepository.GetByMessageIdAsync(channelId, messageId, cancellationToken);
    }

    private static string BuildAttachmentDetails(IReadOnlyList<ChatAttachmentData> attachments, int maxCharacters)
    {
        var remaining = maxCharacters;
        List<string> lines =
        [
            $"과거 채팅에서 문서 {attachments.Count}개를 불러왔습니다.",
            ""
        ];

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            lines.Add($"--- 문서 #{i + 1} ---");
            lines.Add($"ChatAttachmentId: {attachment.Id}");
            lines.Add($"ChatLogId: {attachment.ChatLogId}");
            lines.Add($"MessageId: {attachment.MessageId ?? "(unknown)"}");
            lines.Add($"File: {attachment.FileName ?? "(unknown)"}");
            lines.Add($"Content-Type: {attachment.ContentType}");
            lines.Add($"Size: {attachment.SizeBytes} bytes");
            lines.Add($"Extraction-Status: {attachment.ExtractionStatus}");
            lines.Add($"Link: {BuildMessageReference(attachment)}");

            if (!string.IsNullOrWhiteSpace(attachment.ExtractionError))
            {
                lines.Add($"Extraction-Error: {attachment.ExtractionError}");
            }

            if (string.IsNullOrWhiteSpace(attachment.ExtractedText))
            {
                lines.Add("Extracted-Text: (추출된 텍스트가 없습니다.)");
                lines.Add("");
                continue;
            }

            var text = attachment.ExtractedText.Trim();
            if (text.Length > remaining)
            {
                text = text[..remaining] + "\n...(문서 텍스트가 길어 잘렸습니다.)";
                remaining = 0;
            }
            else
            {
                remaining -= text.Length;
            }

            lines.Add("Extracted-Text:");
            lines.Add(text);
            lines.Add("");

            if (remaining <= 0)
            {
                break;
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildExcerpt(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(추출된 텍스트가 없습니다.)";
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string BuildMessageReference(ChatAttachmentData attachment)
    {
        if (string.IsNullOrEmpty(attachment.MessageId))
        {
            return "(메시지가 오래되어 참조할 수 없어요)";
        }

        var guildPart = string.IsNullOrEmpty(attachment.GuildId) ? "@me" : attachment.GuildId;
        return $"https://discord.com/channels/{guildPart}/{attachment.ChannelId}/{attachment.MessageId}";
    }

    private static string? ExtractMessageId(string? messageIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(messageIdOrUrl))
        {
            return null;
        }

        var trimmed = messageIdOrUrl.Trim();
        if (trimmed.All(char.IsDigit))
        {
            return trimmed;
        }

        var match = DiscordMessageUrlRegex().Match(trimmed);
        return match.Success ? match.Groups["messageId"].Value : null;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryParseOptionalDateTime(
        string? dateString,
        TimeZoneInfo timezone,
        out DateTimeOffset? dateTime,
        out string error)
    {
        dateTime = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                dateString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            error = "날짜/시간을 해석하지 못했습니다. 예: 2026-06-13T15:00:00";
            return false;
        }

        dateTime = parsed.Offset == TimeSpan.Zero && !dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified), timezone.BaseUtcOffset)
            : parsed;
        return true;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    [GeneratedRegex(@"discord(?:app)?\.com/channels/[^/\s]+/[^/\s]+/(?<messageId>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordMessageUrlRegex();
}
