namespace DiscordBot.Repositories;

public record ChatLogData(long Id, string ChannelId, string UserId, string Content, DateTime CreatedAt);

public interface IChatLogRepository
{
    ValueTask AddAsync(string channelId, string userId, string content, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
}
