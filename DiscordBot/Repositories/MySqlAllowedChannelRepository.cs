using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlAllowedChannelRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAllowedChannelRepository
{
    public async ValueTask<IReadOnlyList<AllowedChannelData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `channel_id` AS ChannelId,
    `guild_id` AS GuildId,
    `channel_name` AS ChannelName,
    `guild_name` AS GuildName,
    `memo` AS Memo,
    `enabled` AS Enabled,
    `created_by` AS CreatedBy,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `allowed_channel`
ORDER BY `enabled` DESC, `created_at` DESC";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<AllowedChannelData>(command);
        return results.ToList();
    }

    public async ValueTask<IReadOnlyList<string>> GetEnabledChannelIdsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT `channel_id`
FROM `allowed_channel`
WHERE `enabled` = TRUE";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<string>(command);
        return results.ToList();
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
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `allowed_channel`
    (`channel_id`, `guild_id`, `channel_name`, `guild_name`, `memo`, `enabled`, `created_by`)
VALUES
    (@channelId, @guildId, @channelName, @guildName, @memo, @enabled, @createdBy)";

        var command = new CommandDefinition(
            QUERY,
            new { channelId, guildId, channelName, guildName, memo, enabled, createdBy },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
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
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `allowed_channel`
SET
    `channel_id` = @channelId,
    `guild_id` = @guildId,
    `channel_name` = @channelName,
    `guild_name` = @guildName,
    `memo` = @memo,
    `enabled` = @enabled,
    `updated_at` = NOW()
WHERE `id` = @id";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId, channelName, guildName, memo, enabled },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `allowed_channel`
SET `enabled` = @enabled,
    `updated_at` = NOW()
WHERE `id` = @id";

        var command = new CommandDefinition(QUERY, new { id, enabled }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
DELETE FROM `allowed_channel`
WHERE `id` = @id";

        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
