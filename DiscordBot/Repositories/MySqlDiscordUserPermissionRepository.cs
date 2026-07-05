using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlDiscordUserPermissionRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IDiscordUserPermissionRepository
{
    public async ValueTask<IReadOnlyList<DiscordUserPermissionData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `user_id` AS UserId,
    `display_name` AS DisplayName,
    `permission` AS Permission,
    `enabled` AS Enabled,
    `memo` AS Memo,
    `created_by` AS CreatedBy,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `discord_user_permissions`
ORDER BY `enabled` DESC, `permission` ASC, `created_at` DESC";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<DiscordUserPermissionData>(command);
        return results.ToList();
    }

    public async ValueTask<DiscordUserPermissionData?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `user_id` AS UserId,
    `display_name` AS DisplayName,
    `permission` AS Permission,
    `enabled` AS Enabled,
    `memo` AS Memo,
    `created_by` AS CreatedBy,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `discord_user_permissions`
WHERE `id` = @id
LIMIT 1";

        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<DiscordUserPermissionData>(command);
    }

    public async ValueTask<DiscordUserPermissionData?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `user_id` AS UserId,
    `display_name` AS DisplayName,
    `permission` AS Permission,
    `enabled` AS Enabled,
    `memo` AS Memo,
    `created_by` AS CreatedBy,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `discord_user_permissions`
WHERE `user_id` = @userId
LIMIT 1";

        var command = new CommandDefinition(QUERY, new { userId }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<DiscordUserPermissionData>(command);
    }

    public async ValueTask AddAsync(
        string userId,
        string? displayName,
        string permission,
        bool enabled,
        string? memo,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `discord_user_permissions`
    (`user_id`, `display_name`, `permission`, `enabled`, `memo`, `created_by`)
VALUES
    (@userId, @displayName, @permission, @enabled, @memo, @createdBy)";

        var command = new CommandDefinition(
            QUERY,
            new { userId, displayName, permission, enabled, memo, createdBy },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask UpdateAsync(
        long id,
        string userId,
        string? displayName,
        string permission,
        bool enabled,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `discord_user_permissions`
SET
    `user_id` = @userId,
    `display_name` = @displayName,
    `permission` = @permission,
    `enabled` = @enabled,
    `memo` = @memo,
    `updated_at` = NOW()
WHERE `id` = @id";

        var command = new CommandDefinition(
            QUERY,
            new { id, userId, displayName, permission, enabled, memo },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
DELETE FROM `discord_user_permissions`
WHERE `id` = @id";

        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
