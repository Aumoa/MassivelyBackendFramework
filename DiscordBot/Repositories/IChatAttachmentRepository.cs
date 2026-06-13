namespace DiscordBot.Repositories;

public record ChatAttachmentData(
    long Id,
    long ChatLogId,
    string? MessageId,
    string? GuildId,
    string ChannelId,
    string UserId,
    string Content,
    string? DiscordAttachmentId,
    string? FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string? ExtractedText,
    string ExtractionStatus,
    string? ExtractionError,
    DateTime CreatedAt);

public interface IChatAttachmentRepository
{
    ValueTask<ChatAttachmentData?> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatAttachmentData>> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatAttachmentData>> SearchAsync(
        string channelId,
        IReadOnlyList<string> keywords,
        int limit,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}
