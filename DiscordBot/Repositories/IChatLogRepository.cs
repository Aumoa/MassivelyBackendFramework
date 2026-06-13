namespace DiscordBot.Repositories;

public record ChatLogData(long Id, string? MessageId, string? GuildId, string ChannelId, string UserId, string Content, DateTime CreatedAt);

public record ChatLogImageInput(
    string? FileName,
    string ContentType,
    int Width,
    int Height,
    byte[] Data);

public record ChatLogAttachmentInput(
    string? DiscordAttachmentId,
    string? FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    byte[] Data,
    string? ExtractedText,
    string ExtractionStatus,
    string? ExtractionError);

public interface IChatLogRepository
{
    ValueTask AddAsync(
        string? messageId,
        string? guildId,
        string channelId,
        string userId,
        string content,
        IReadOnlyList<ChatLogImageInput>? images = null,
        IReadOnlyList<ChatLogAttachmentInput>? attachments = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> SearchAsync(string channelId, IReadOnlyList<string> keywords, int limit,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetContextAsync(
        string channelId,
        long chatLogId,
        int before,
        int after,
        CancellationToken cancellationToken = default);
}
