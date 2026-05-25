using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal interface IAllowedChannelRequestService
{
    ValueTask<AllowedChannelRequestData> GetOrCreateAsync(
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string requesterId,
        string? requesterName,
        string? messageId,
        CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelRequestData?> GetAsync(string token, CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelData?> ApproveAsync(
        string token,
        string? approvedBy,
        CancellationToken cancellationToken = default);
}

internal sealed class AllowedChannelRequestService(
    IAllowedChannelRequestRepository repository,
    IAllowedChannelService allowedChannels) : IAllowedChannelRequestService
{
    private const int ExpirationDays = 7;
    private const string PendingStatus = "pending";

    public async ValueTask<AllowedChannelRequestData> GetOrCreateAsync(
        string channelId,
        string? guildId,
        string? channelName,
        string? guildName,
        string requesterId,
        string? requesterName,
        string? messageId,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannelId = NormalizeRequired(channelId);
        var now = DateTime.UtcNow;
        var existing = await repository.GetPendingByChannelIdAsync(normalizedChannelId, now, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var token = Guid.NewGuid().ToString("N");
        var input = new AllowedChannelRequestInput(
            token,
            NormalizeOptional(guildId),
            normalizedChannelId,
            NormalizeOptional(guildName),
            NormalizeOptional(channelName),
            NormalizeRequired(requesterId),
            NormalizeOptional(requesterName),
            NormalizeOptional(messageId),
            now.AddDays(ExpirationDays));

        await repository.AddAsync(input, cancellationToken);
        return await repository.GetByTokenAsync(token, cancellationToken)
            ?? throw new InvalidOperationException("Failed to create allowed channel request.");
    }

    public ValueTask<AllowedChannelRequestData?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        return repository.GetByTokenAsync(NormalizeRequired(token), cancellationToken);
    }

    public async ValueTask<AllowedChannelData?> ApproveAsync(
        string token,
        string? approvedBy,
        CancellationToken cancellationToken = default)
    {
        var request = await repository.GetByTokenAsync(NormalizeRequired(token), cancellationToken);
        if (request == null)
        {
            return null;
        }

        if (!string.Equals(request.Status, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return await allowedChannels.GetByChannelIdAsync(request.ChannelId, cancellationToken);
        }

        if (request.ExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("This channel request has expired.");
        }

        var existing = await allowedChannels.GetByChannelIdAsync(request.ChannelId, cancellationToken);
        var memo = $"승인 요청 #{request.Id}에서 추가됨";
        if (existing == null)
        {
            await allowedChannels.AddAsync(
                request.ChannelId,
                request.GuildId,
                request.ChannelName,
                request.GuildName,
                memo,
                enabled: true,
                NormalizeOptional(approvedBy),
                cancellationToken);
        }
        else
        {
            await allowedChannels.UpdateAsync(
                existing.Id,
                request.ChannelId,
                request.GuildId ?? existing.GuildId,
                request.ChannelName ?? existing.ChannelName,
                request.GuildName ?? existing.GuildName,
                string.IsNullOrWhiteSpace(existing.Memo) ? memo : existing.Memo,
                enabled: true,
                cancellationToken);
        }

        await repository.MarkApprovedAsync(request.Id, NormalizeOptional(approvedBy), cancellationToken);
        return await allowedChannels.GetByChannelIdAsync(request.ChannelId, cancellationToken);
    }

    private static string NormalizeRequired(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Required value is empty.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
