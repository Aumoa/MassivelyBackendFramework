namespace DiscordBot.Repositories;

public record AppointmentData(
    long Id,
    string? GuildId,
    string ChannelId,
    string UserId,
    string? SourceMessageId,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    bool HasTime,
    string Timezone,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime ExpiresAtUtc);

public record AppointmentInput(
    string? GuildId,
    string ChannelId,
    string UserId,
    string? SourceMessageId,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    bool HasTime,
    string Timezone,
    DateTime ExpiresAtUtc);

public interface IAppointmentRepository
{
    ValueTask<long> AddAsync(AppointmentInput input, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AppointmentData>> GetActiveAsync(
        string userId,
        string? guildId,
        DateTime nowUtc,
        int limit,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        bool includePast = false,
        CancellationToken cancellationToken = default);

    ValueTask<AppointmentData?> GetActiveByIdAsync(
        long id,
        string userId,
        string? guildId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    ValueTask<bool> UpdateAsync(
        long id,
        string userId,
        string? guildId,
        string title,
        string? description,
        DateTime startsAtUtc,
        bool hasTime,
        string timezone,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        long id,
        string userId,
        string? guildId,
        CancellationToken cancellationToken = default);

    ValueTask<int> ExpireOldAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
