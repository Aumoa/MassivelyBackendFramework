using System.Globalization;
using System.Text.RegularExpressions;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal static partial class DiscordChatAttachmentToolFormatter
{
    public static string BuildAttachmentDetails(IReadOnlyList<ChatAttachmentData> attachments, int maxCharacters)
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

    public static string BuildSearchResults(
        IReadOnlyList<string> keywords,
        IReadOnlyList<ChatAttachmentData> results,
        TimeZoneInfo timezone)
    {
        List<string> lines =
        [
            $"검색 키워드: {string.Join(", ", keywords)}",
            $"결과 {results.Count}건:",
            ""
        ];

        for (var i = 0; i < results.Count; i++)
        {
            var attachment = results[i];
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(attachment.CreatedAt), timezone);
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

    public static string BuildExcerpt(string? text, int maxLength)
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

    public static string BuildMessageReference(ChatAttachmentData attachment)
    {
        if (string.IsNullOrEmpty(attachment.MessageId))
        {
            return "(메시지가 오래되어 참조할 수 없어요)";
        }

        var guildPart = string.IsNullOrEmpty(attachment.GuildId) ? "@me" : attachment.GuildId;
        return $"https://discord.com/channels/{guildPart}/{attachment.ChannelId}/{attachment.MessageId}";
    }

    public static string? ExtractMessageId(string? messageIdOrUrl)
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

    public static TimeZoneInfo ResolveTimeZone(string? timezone)
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

    public static bool TryParseOptionalDateTime(
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
