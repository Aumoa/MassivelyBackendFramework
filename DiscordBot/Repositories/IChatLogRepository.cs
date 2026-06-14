namespace DiscordBot.Repositories;

public record ChatLogData(
    long Id,
    string? MessageId,
    string? GuildId,
    string ChannelId,
    string UserId,
    string Content,
    DateTime CreatedAt,
    string? ReferencedMessageId = null,
    string? ReferencedChannelId = null,
    string? ReferencedGuildId = null);

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
        string? referencedMessageId = null,
        string? referencedChannelId = null,
        string? referencedGuildId = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> SearchAsync(
        string channelId,
        IReadOnlyList<string> keywords,
        int limit,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? authorUserId = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetContextAsync(
        string channelId,
        long chatLogId,
        int before,
        int after,
        CancellationToken cancellationToken = default);

    ValueTask<ChatLogData?> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatLogData>> GetRepliesAsync(
        string channelId,
        string referencedMessageId,
        int limit,
        CancellationToken cancellationToken = default);
}
