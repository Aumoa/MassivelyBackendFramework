using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal class DiscordTools(SocketSelfUser selfUser, SocketMessage message, IChatLogRepository chatLogRepository)
{
    private static readonly TimeZoneInfo s_KST = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");

    [ToolFunction(
        Name = "get_chat_history",
        Description = "현재 채팅방의 채팅 기록을 MySQL에서 조회합니다. offset으로 건너뛸 메시지 수, limit으로 가져올 메시지 수를 지정할 수 있으며, from_date/to_date로 특정 기간을 필터링할 수 있습니다. 날짜/시간은 한국 표준시(KST, UTC+9) 기준입니다. 대화 요약, 분위기 파악, 이전 맥락 확인이 필요할 때 사용합니다.")]
    public async Task<string> GetChatHistoryAsync(
        [ToolParameterInfo(Description = "가져올 최근 메시지 수 (최대 100개)")]
        int limit,
        [ToolParameterInfo(Description = "건너뛸 메시지 수 (기본값: 0)")]
        int offset = 0,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (한국 표준시 기준, 예: 2026-04-01T00:00:00). 미지정 시 제한 없음")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (한국 표준시 기준, 예: 2026-04-09T23:59:59). 미지정 시 제한 없음")]
        string? to_date = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 100) limit = 100;
        if (limit < 1) limit = 1;
        if (offset < 0) offset = 0;

        DateTimeOffset? from = !string.IsNullOrEmpty(from_date) ? ParseAsKST(from_date) : null;
        DateTimeOffset? to = !string.IsNullOrEmpty(to_date) ? ParseAsKST(to_date) : null;

        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.GetAsync(channelId, limit, offset, from, to, cancellationToken);

        if (logs.Count == 0)
            return "조회된 메시지가 없습니다.";

        var selfId = selfUser.Id.ToString();
        List<string> lines = [];

        foreach (var log in logs)
        {
            var kstTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, s_KST);
            if (log.UserId == selfId)
                lines.Add($"[{kstTime:yyyy-MM-dd HH:mm:ss}](나의 응답): {log.Content}");
            else
                lines.Add($"[{kstTime:yyyy-MM-dd HH:mm:ss}](사용자 {log.UserId}의 메시지): {log.Content}");
        }

        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "get_current_date",
        Description = "현재 날짜와 시간을 한국 표준시(KST, UTC+9) 기준으로 가져옵니다.")]
    public Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default)
    {
        var kstNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, s_KST);
        return Task.FromResult(kstNow.ToString("yyyy-MM-dd HH:mm:ss (KST)"));
    }

    private static DateTimeOffset ParseAsKST(string dateString)
    {
        if (DateTimeOffset.TryParse(dateString, out var parsed))
        {
            if (parsed.Offset == TimeSpan.Zero && !dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return new DateTimeOffset(parsed.DateTime, TimeSpan.FromHours(9));
            return parsed;
        }

        var dt = DateTime.Parse(dateString);
        return new DateTimeOffset(dt, TimeSpan.FromHours(9));
    }
}
