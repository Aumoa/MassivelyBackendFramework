using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlAmbientChatContextSettingsRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAmbientChatContextSettingsRepository
{
    public async ValueTask<AmbientChatContextSettingsData?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `enabled` AS Enabled,
    `window_message_count` AS WindowMessageCount,
    `window_max_chars` AS WindowMaxChars,
    `lookback_minutes` AS LookbackMinutes,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `ambient_chat_context_settings`
WHERE `id` = 1";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AmbientChatContextSettingsData>(command);
    }

    public async ValueTask UpsertAsync(
        bool enabled,
        int windowMessageCount,
        int windowMaxChars,
        int lookbackMinutes,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `ambient_chat_context_settings`
    (`id`, `enabled`, `window_message_count`, `window_max_chars`, `lookback_minutes`)
VALUES
    (1, @enabled, @windowMessageCount, @windowMaxChars, @lookbackMinutes)
ON DUPLICATE KEY UPDATE
    `enabled` = @enabled,
    `window_message_count` = @windowMessageCount,
    `window_max_chars` = @windowMaxChars,
    `lookback_minutes` = @lookbackMinutes,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { enabled, windowMessageCount, windowMaxChars, lookbackMinutes },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
