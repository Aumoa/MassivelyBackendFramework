using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Services;

internal interface IAllowedChannelService
{
    ValueTask<bool> IsAllowedAsync(string channelId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AllowedChannelData>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelData?> GetAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelData?> GetByChannelIdAsync(string channelId, CancellationToken cancellationToken = default);

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

internal sealed class AllowedChannelService(IAllowedChannelRepository repository, IMemoryCache cache) : IAllowedChannelService
{
    private const string CacheKey = "DiscordBot.AllowedChannel.EnabledIds";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async ValueTask<bool> IsAllowedAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var normalizedChannelId = NormalizeRequired(channelId);
        var channelIds = await GetCachedEnabledChannelIdsAsync(cancellationToken);
        return channelIds.Contains(normalizedChannelId);
    }

    public ValueTask<IReadOnlyList<AllowedChannelData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return repository.GetAllAsync(cancellationToken);
    }

    public ValueTask<AllowedChannelData?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        return repository.GetAsync(id, cancellationToken);
    }

    public ValueTask<AllowedChannelData?> GetByChannelIdAsync(string channelId, CancellationToken cancellationToken = default)
    {
        return repository.GetByChannelIdAsync(NormalizeRequired(channelId), cancellationToken);
    }

    public async ValueTask AddAsync(
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string? memo,
        bool enabled,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        await repository.AddAsync(
            NormalizeRequired(channelId),
            NormalizeOptional(guildId),
            NormalizeOptional(channelName),
            NormalizeOptional(guildName),
            NormalizeOptional(memo),
            enabled,
            NormalizeOptional(createdBy),
            cancellationToken);
        InvalidateCache();
    }

    public async ValueTask UpdateAsync(
        long id,
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string? memo,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await repository.UpdateAsync(
            id,
            NormalizeRequired(channelId),
            NormalizeOptional(guildId),
            NormalizeOptional(channelName),
            NormalizeOptional(guildName),
            NormalizeOptional(memo),
            enabled,
            cancellationToken);
        InvalidateCache();
    }

    public async ValueTask SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        await repository.SetEnabledAsync(id, enabled, cancellationToken);
        InvalidateCache();
    }

    public async ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await repository.DeleteAsync(id, cancellationToken);
        InvalidateCache();
    }

    private async ValueTask<HashSet<string>> GetCachedEnabledChannelIdsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<HashSet<string>>(CacheKey, out var cachedChannelIds) && cachedChannelIds != null)
        {
            return cachedChannelIds;
        }

        var channelIds = await repository.GetEnabledChannelIdsAsync(cancellationToken);
        var set = channelIds
            .Select(NormalizeOptional)
            .Where(id => id != null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        cache.Set(CacheKey, set, CacheDuration);
        return set;
    }

    private void InvalidateCache()
    {
        cache.Remove(CacheKey);
    }

    private static string NormalizeRequired(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("채널 ID가 필요합니다.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
