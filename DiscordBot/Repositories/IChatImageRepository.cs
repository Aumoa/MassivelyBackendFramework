namespace DiscordBot.Repositories;

public record ChatImageData(
    long Id,
    long ChatLogId,
    string? MessageId,
    string? GuildId,
    string ChannelId,
    string UserId,
    string Content,
    string? FileName,
    string ContentType,
    int Width,
    int Height,
    byte[] Data,
    DateTime CreatedAt);

public interface IChatImageRepository
{
    ValueTask<ChatImageData?> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatImageData>> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<ChatImageData?> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatImageData>> GetByMessageIdsAsync(
        string channelId,
        IReadOnlyList<string> messageIds,
        int limit,
        CancellationToken cancellationToken = default);
}
