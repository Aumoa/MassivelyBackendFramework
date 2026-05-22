using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlToolSettingsRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IToolSettingsRepository
{
    public async ValueTask<IReadOnlyList<ToolSettingData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `tool_name` AS ToolName,
    `enabled` AS Enabled,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `tool_settings`";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ToolSettingData>(command);
        return results.ToList();
    }

    public async ValueTask<ToolSettingData?> GetAsync(string toolName, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `tool_name` AS ToolName,
    `enabled` AS Enabled,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `tool_settings`
WHERE `tool_name` = @toolName
LIMIT 1";

        var command = new CommandDefinition(QUERY, new { toolName }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ToolSettingData>(command);
    }

    public async ValueTask UpsertAsync(string toolName, bool enabled, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `tool_settings`
    (`tool_name`, `enabled`)
VALUES
    (@toolName, @enabled)
ON DUPLICATE KEY UPDATE
    `enabled` = @enabled,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { toolName, enabled },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
