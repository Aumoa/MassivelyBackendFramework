namespace DiscordBot.Repositories;

public record AllowedChannelData(
    long Id,
    string ChannelId,
    string? GuildId,
    string? ChannelName,
    string? GuildName,
    string? Memo,
    bool Enabled,
    string? CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IAllowedChannelRepository
{
    ValueTask<IReadOnlyList<AllowedChannelData>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelData?> GetAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelData?> GetByChannelIdAsync(string channelId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> GetEnabledChannelIdsAsync(CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string? memo,
        bool enabled,
        string? createdBy,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(
        long id,
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string? memo,
        bool enabled,
        CancellationToken cancellationToken = default);

    ValueTask SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default);
}
