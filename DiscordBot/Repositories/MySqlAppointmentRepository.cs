using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlAppointmentRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAppointmentRepository
{
    public async ValueTask<long> AddAsync(AppointmentInput input, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY = @"
INSERT INTO `appointment`
    (`guild_id`, `channel_id`, `user_id`, `source_message_id`, `title`, `description`, `starts_at_utc`, `has_time`, `timezone`, `expires_at_utc`)
VALUES
    (@GuildId, @ChannelId, @UserId, @SourceMessageId, @Title, @Description, @StartsAtUtc, @HasTime, @Timezone, @ExpiresAtUtc)";

        var command = new CommandDefinition(QUERY, input, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        var idCommand = new CommandDefinition(
            "SELECT LAST_INSERT_ID()",
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<long>(idCommand);
    }

    public async ValueTask<IReadOnlyList<AppointmentData>> GetActiveAsync(
        string channelId,
        string? guildId,
        DateTime nowUtc,
        int limit,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        bool includePast = false,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT
    `id` AS Id,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `user_id` AS UserId,
    `source_message_id` AS SourceMessageId,
    `title` AS Title,
    `description` AS Description,
    `starts_at_utc` AS StartsAtUtc,
    `has_time` AS HasTime,
    `timezone` AS Timezone,
    `status` AS Status,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt,
    `expires_at_utc` AS ExpiresAtUtc
FROM `appointment`
WHERE `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'
  AND `expires_at_utc` > @nowUtc");

        if (!includePast)
        {
            queryBuilder.Append(" AND `starts_at_utc` >= @nowUtc");
        }

        if (fromUtc.HasValue)
        {
            queryBuilder.Append(" AND `starts_at_utc` >= @fromUtc");
        }

        if (toUtc.HasValue)
        {
            queryBuilder.Append(" AND `starts_at_utc` <= @toUtc");
        }

        queryBuilder.Append(" ORDER BY `starts_at_utc` ASC LIMIT @limit");

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new { channelId, guildId, nowUtc, limit, fromUtc, toUtc },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<AppointmentData>(command);
        return results.ToList();
    }

    public async ValueTask<AppointmentData?> GetActiveByIdAsync(
        long id,
        string channelId,
        string? guildId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `user_id` AS UserId,
    `source_message_id` AS SourceMessageId,
    `title` AS Title,
    `description` AS Description,
    `starts_at_utc` AS StartsAtUtc,
    `has_time` AS HasTime,
    `timezone` AS Timezone,
    `status` AS Status,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt,
    `expires_at_utc` AS ExpiresAtUtc
FROM `appointment`
WHERE `id` = @id
  AND `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'
  AND `expires_at_utc` > @nowUtc
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId, nowUtc },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<AppointmentData>(command);
    }

    public async ValueTask<bool> UpdateAsync(
        long id,
        string channelId,
        string? guildId,
        string title,
        string? description,
        DateTime startsAtUtc,
        bool hasTime,
        string timezone,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `appointment`
SET
    `title` = @title,
    `description` = @description,
    `starts_at_utc` = @startsAtUtc,
    `has_time` = @hasTime,
    `timezone` = @timezone,
    `expires_at_utc` = @expiresAtUtc,
    `updated_at` = NOW()
WHERE `id` = @id
  AND `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId, title, description, startsAtUtc, hasTime, timezone, expiresAtUtc },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) > 0;
    }

    public async ValueTask<bool> DeleteAsync(
        long id,
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `appointment`
SET
    `status` = 'deleted',
    `updated_at` = NOW()
WHERE `id` = @id
  AND `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) > 0;
    }

    public async ValueTask<long> AddItemAsync(AppointmentItemInput input, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY = @"
INSERT INTO `appointment_item`
    (`appointment_id`, `item_type`, `created_by_user_id`, `content`, `sort_order`)
SELECT
    @AppointmentId,
    @ItemType,
    @CreatedByUserId,
    @Content,
    COALESCE(MAX(`sort_order`), 0) + 1
FROM `appointment_item`
WHERE `appointment_id` = @AppointmentId
  AND `item_type` = @ItemType
  AND `status` = 'active'";

        var command = new CommandDefinition(QUERY, input, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        var idCommand = new CommandDefinition(
            "SELECT LAST_INSERT_ID()",
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<long>(idCommand);
    }

    public async ValueTask<IReadOnlyList<AppointmentItemData>> GetActiveItemsAsync(
        long appointmentId,
        string? itemType = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT
    `id` AS Id,
    `appointment_id` AS AppointmentId,
    `item_type` AS ItemType,
    `created_by_user_id` AS CreatedByUserId,
    `content` AS Content,
    `status` AS Status,
    `sort_order` AS SortOrder,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `appointment_item`
WHERE `appointment_id` = @appointmentId
  AND `status` = 'active'");

        if (!string.IsNullOrWhiteSpace(itemType))
        {
            queryBuilder.Append(" AND `item_type` = @itemType");
        }

        queryBuilder.Append(" ORDER BY `item_type`, `sort_order`, `id`");

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new { appointmentId, itemType },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<AppointmentItemData>(command);
        return results.ToList();
    }

    public async ValueTask<bool> DeleteItemAsync(
        long appointmentId,
        long itemId,
        string? itemType = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
UPDATE `appointment_item`
SET
    `status` = 'deleted',
    `updated_at` = NOW()
WHERE `appointment_id` = @appointmentId
  AND `id` = @itemId
  AND `status` = 'active'");

        if (!string.IsNullOrWhiteSpace(itemType))
        {
            queryBuilder.Append(" AND `item_type` = @itemType");
        }

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new { appointmentId, itemId, itemType },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) > 0;
    }

    public async ValueTask<int> ExpireOldAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `appointment`
SET
    `status` = 'expired',
    `updated_at` = NOW()
WHERE `status` = 'active'
  AND `expires_at_utc` <= @nowUtc";

        var command = new CommandDefinition(QUERY, new { nowUtc }, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }
}
