using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal class DiscordTools(SocketSelfUser selfUser, SocketMessage message, IChatLogRepository chatLogRepository)
{
    [ToolFunction(
        Name = "get_chat_history",
        Description = "현재 채팅방의 채팅 기록을 MySQL에서 조회합니다. offset으로 건너뛸 메시지 수, limit으로 가져올 메시지 수를 지정할 수 있으며, from_date/to_date로 특정 기간을 필터링할 수 있습니다. 사용자의 언어에 맞는 timezone을 지정하세요 (한국어: Asia/Seoul, 영어(미국): America/New_York 등). 기본값은 UTC입니다.")]
    public async Task<string> GetChatHistoryAsync(
        [ToolParameterInfo(Description = "가져올 최근 메시지 수 (최대 100개)")]
        int limit,
        [ToolParameterInfo(Description = "건너뛸 메시지 수 (기본값: 0)")]
        int offset = 0,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (지정된 timezone 기준, 예: 2026-04-01T00:00:00). 미지정 시 제한 없음")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (지정된 timezone 기준, 예: 2026-04-09T23:59:59). 미지정 시 제한 없음")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York, Europe/London). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 100) limit = 100;
        if (limit < 1) limit = 1;
        if (offset < 0) offset = 0;

        var tz = ResolveTimeZone(timezone);
        var utcOffset = tz.BaseUtcOffset;

        DateTimeOffset? from = !string.IsNullOrEmpty(from_date) ? ParseInTimeZone(from_date, utcOffset) : null;
        DateTimeOffset? to = !string.IsNullOrEmpty(to_date) ? ParseInTimeZone(to_date, utcOffset) : null;

        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.GetAsync(channelId, limit, offset, from, to, cancellationToken);

        if (logs.Count == 0)
            return "조회된 메시지가 없습니다.";

        var selfId = selfUser.Id.ToString();
        List<string> lines = [];

        foreach (var log in logs)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, tz);
            if (log.UserId == selfId)
                lines.Add($"[{localTime:yyyy-MM-dd HH:mm:ss}](나의 응답): {log.Content}");
            else
                lines.Add($"[{localTime:yyyy-MM-dd HH:mm:ss}](사용자 {log.UserId}의 메시지): {log.Content}");
        }

        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "search_chat_history",
        Description = @"현재 채팅방의 채팅 기록을 키워드로 검색합니다. 사용자 자연어 요청에서 검색에 도움이 될 키워드(동의어, 관련어 포함)를 여러 개 추출하여 콤마로 구분해 전달하세요. 각 키워드는 2글자 이상이어야 하며 OR 검색으로 동작합니다.

[중요 — 결과 응답 작성 규칙]
각 결과에는 'Link: https://...' 형식의 메시지 링크가 포함됩니다. 이 URL을 사용자에게 보여주는 답변 본문에 **반드시 그대로(전체 URL을) 복사해서 적어야** Discord가 자동으로 원본 메시지를 인용 카드로 표시합니다.

- ❌ 잘못된 예: '위 링크에서 확인할 수 있어요' (URL을 적지 않으면 사용자에게는 아무것도 보이지 않음)
- ❌ 잘못된 예: '[원본 메시지](링크)' 같이 마크다운 링크로 감싸지 마세요 (인용 카드가 표시되지 않음)
- ✅ 올바른 예: '관련 대화: https://discord.com/channels/123/456/789'
- ✅ 올바른 예: 답변 마지막 줄에 https://discord.com/channels/123/456/789 를 그대로 한 줄로 넣기

Link 값이 '(메시지가 오래되어 참조할 수 없어요)'로 표시되어 있다면 URL이 없으므로 그 문구를 그대로 본문에 인용해서 사용자에게 안내하세요.")]
    public async Task<string> SearchChatHistoryAsync(
        [ToolParameterInfo(Description = "검색할 키워드들을 콤마(,)로 구분한 문자열. 동의어와 관련어를 함께 넣어서 누락을 줄이세요. 각 키워드는 2글자 이상. 예: \"피자, 도미노, 배달\"")]
        string keywords,
        [ToolParameterInfo(Description = "최대 결과 수 (1~50, 기본 20)")]
        int limit = 20,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (지정된 timezone 기준, 예: 2026-04-01T00:00:00). 미지정 시 제한 없음")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (지정된 timezone 기준, 예: 2026-04-09T23:59:59). 미지정 시 제한 없음")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 50) limit = 50;
        if (limit < 1) limit = 1;

        var tz = ResolveTimeZone(timezone);
        var utcOffset = tz.BaseUtcOffset;

        DateTimeOffset? from = !string.IsNullOrEmpty(from_date) ? ParseInTimeZone(from_date, utcOffset) : null;
        DateTimeOffset? to = !string.IsNullOrEmpty(to_date) ? ParseInTimeZone(to_date, utcOffset) : null;

        var validKeywords = (keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length >= 2)
            .ToList();
        if (validKeywords.Count == 0)
            return "검색에 사용할 2글자 이상의 키워드가 필요합니다.";

        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.SearchAsync(channelId, validKeywords, limit, from, to, cancellationToken);

        if (logs.Count == 0)
            return "검색 결과가 없습니다.";

        var selfId = selfUser.Id.ToString();
        List<string> lines = [$"검색 키워드: {string.Join(", ", validKeywords)}", $"결과 {logs.Count}건:", ""];

        int index = 1;
        foreach (var log in logs)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, tz);
            var author = log.UserId == selfId ? "나의 응답" : $"사용자 {log.UserId}의 메시지";
            var reference = BuildMessageReference(log);
            lines.Add($"--- 결과 #{index} ---");
            lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Author: {author}");
            lines.Add($"Link: {reference}");
            lines.Add($"Content: {log.Content}");
            lines.Add("");
            index++;
        }

        return string.Join("\n", lines);
    }

    private static string BuildMessageReference(ChatLogData log)
    {
        if (string.IsNullOrEmpty(log.MessageId))
            return "(메시지가 오래되어 참조할 수 없어요)";

        var guildPart = string.IsNullOrEmpty(log.GuildId) ? "@me" : log.GuildId;
        return $"https://discord.com/channels/{guildPart}/{log.ChannelId}/{log.MessageId}";
    }

    [ToolFunction(
        Name = "get_current_date",
        Description = "현재 날짜와 시간을 가져옵니다. 사용자의 언어에 맞는 timezone을 지정하세요 (한국어: Asia/Seoul, 영어(미국): America/New_York 등). 기본값은 UTC입니다.")]
    public Task<string> GetCurrentDateAsync(
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York, Europe/London). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        var tz = ResolveTimeZone(timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return Task.FromResult($"{now:yyyy-MM-dd HH:mm:ss} ({tz.Id})");
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrEmpty(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset ParseInTimeZone(string dateString, TimeSpan utcOffset)
    {
        if (DateTimeOffset.TryParse(dateString, out var parsed))
        {
            if (parsed.Offset == TimeSpan.Zero && !dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return new DateTimeOffset(parsed.DateTime, utcOffset);
            return parsed;
        }

        var dt = DateTime.Parse(dateString);
        return new DateTimeOffset(dt, utcOffset);
    }
}
