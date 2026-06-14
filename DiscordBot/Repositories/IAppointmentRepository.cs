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

public record AppointmentItemData(
    long Id,
    long AppointmentId,
    string ItemType,
    string CreatedByUserId,
    string Content,
    string Status,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record AppointmentItemInput(
    long AppointmentId,
    string ItemType,
    string CreatedByUserId,
    string Content);

public interface IAppointmentRepository
{
    ValueTask<long> AddAsync(AppointmentInput input, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AppointmentData>> GetActiveAsync(
        string channelId,
        string? guildId,
        DateTime nowUtc,
        int limit,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        bool includePast = false,
        CancellationToken cancellationToken = default);

    ValueTask<AppointmentData?> GetActiveByIdAsync(
        long id,
        string channelId,
        string? guildId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    ValueTask<bool> UpdateAsync(
        long id,
        string channelId,
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
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default);

    ValueTask<long> AddItemAsync(AppointmentItemInput input, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AppointmentItemData>> GetActiveItemsAsync(
        long appointmentId,
        string? itemType = null,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteItemAsync(
        long appointmentId,
        long itemId,
        string? itemType = null,
        CancellationToken cancellationToken = default);

    ValueTask<int> ExpireOldAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
