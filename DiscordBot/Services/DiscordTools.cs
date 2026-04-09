using AI;
using Discord;
using Discord.WebSocket;

namespace DiscordBot.Services;

internal class DiscordTools(SocketSelfUser selfUser, SocketMessage message)
{
    [ToolFunction(
        Name = "get_chat_history",
        Description = "현재 채팅방의 채팅 목록 중 마지막 N개를 가져옵니다. 대화 요약, 분위기 파악, 이전 맥락 확인이 필요할 때 사용합니다. AI와 직접 대화한 내용과 별개로, 현재 채팅방의 모든 대화를 조회합니다.")]
    public async Task<string> GetChatHistoryAsync(
        [ToolParameterInfo(Description = "가져올 최근 메시지 수 (최대 100개)")]
        int limit,
        CancellationToken cancellationToken = default)
    {
        List<string> totalMessages = [];

        await foreach (var dmc in message.Channel.GetMessagesAsync(limit, CacheMode.AllowDownload))
        {
            foreach (var dm in dmc)
            {
                if (dm.Author.Id == selfUser.Id)
                {
                    totalMessages.Add($"[{dm.CreatedAt}](나의 응답): {dm.Content}");
                }
                else
                {
                    totalMessages.Add($"[{dm.CreatedAt}]({dm.Author.Username}님의 메시지): {dm.Content}");
                }
            }
        }

        return string.Join("\n", totalMessages);
    }

    [ToolFunction(
        Name = "get_current_date",
        Description = "현재 날짜와 시간을 UTC 기준으로 가져옵니다.")]
    public Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DateTimeOffset.UtcNow.ToString());
    }
}
