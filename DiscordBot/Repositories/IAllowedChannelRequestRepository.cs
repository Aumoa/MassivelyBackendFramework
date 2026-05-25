namespace DiscordBot.Repositories;

public record AllowedChannelRequestData(
    long Id,
    string Token,
    string? GuildId,
    string ChannelId,
    string? GuildName,
    string? ChannelName,
    string RequesterId,
    string? RequesterName,
    string? MessageId,
    string Status,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public record AllowedChannelRequestInput(
    string Token,
    string? GuildId,
    string ChannelId,
    string? GuildName,
    string? ChannelName,
    string RequesterId,
    string? RequesterName,
    string? MessageId,
    DateTime ExpiresAt);

public interface IAllowedChannelRequestRepository
{
    ValueTask<AllowedChannelRequestData?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    ValueTask<AllowedChannelRequestData?> GetPendingByChannelIdAsync(
        string channelId,
        DateTime now,
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(AllowedChannelRequestInput request, CancellationToken cancellationToken = default);

    ValueTask MarkApprovedAsync(long id, string? approvedBy, CancellationToken cancellationToken = default);
}
