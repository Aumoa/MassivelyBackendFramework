namespace DiscordBot.Repositories;

public record ChannelNoteData(
    long Id,
    string? GuildId,
    string ChannelId,
    string CreatedByUserId,
    string Title,
    string Content,
    string? Tags,
    string? SourceMessageId,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ChannelNoteInput(
    string? GuildId,
    string ChannelId,
    string CreatedByUserId,
    string Title,
    string Content,
    string? Tags,
    string? SourceMessageId);

public interface IChannelNoteRepository
{
    ValueTask<long> AddAsync(ChannelNoteInput input, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChannelNoteData>> GetActiveAsync(
        string channelId,
        string? guildId,
        int limit,
        string? query = null,
        CancellationToken cancellationToken = default);

    ValueTask<ChannelNoteData?> GetActiveByIdAsync(
        long id,
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        long id,
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default);
}
