using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlDailyImagePostSettingsRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IDailyImagePostSettingsRepository
{
    public async ValueTask<DailyImagePostSettingsData?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `enabled` AS Enabled,
    `channel_id` AS ChannelId,
    `post_time_of_day` AS PostTimeOfDay,
    `theme_prompt` AS ThemePrompt,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `daily_image_post_settings`
WHERE `id` = 1";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DailyImagePostSettingsData>(command);
    }

    public async ValueTask UpsertAsync(
        bool enabled,
        string channelId,
        string postTimeOfDay,
        string themePrompt,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `daily_image_post_settings`
    (`id`, `enabled`, `channel_id`, `post_time_of_day`, `theme_prompt`)
VALUES
    (1, @enabled, @channelId, @postTimeOfDay, @themePrompt)
ON DUPLICATE KEY UPDATE
    `enabled` = @enabled,
    `channel_id` = @channelId,
    `post_time_of_day` = @postTimeOfDay,
    `theme_prompt` = @themePrompt,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                enabled,
                channelId,
                postTimeOfDay,
                themePrompt
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
