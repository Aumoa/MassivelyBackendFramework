using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlAutoResponseSettingsRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAutoResponseSettingsRepository, IAutoResponseEventRepository
{
    public async ValueTask<AutoResponseSettingsData?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `enabled` AS Enabled,
    `interval_seconds` AS IntervalSeconds,
    `cooldown_seconds` AS CooldownSeconds,
    `max_buffered_messages` AS MaxBufferedMessages,
    `classifier_max_tokens` AS ClassifierMaxTokens,
    `classifier_model` AS ClassifierModel,
    `bot_name_aliases_json` AS BotNameAliasesJson,
    `classifier_guidelines` AS ClassifierGuidelines,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `auto_response_settings`
WHERE `id` = 1";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AutoResponseSettingsData>(command);
    }

    public async ValueTask UpsertAsync(
        bool enabled,
        int intervalSeconds,
        int cooldownSeconds,
        int maxBufferedMessages,
        int classifierMaxTokens,
        string? classifierModel,
        string botNameAliasesJson,
        string? classifierGuidelines,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `auto_response_settings`
    (`id`, `enabled`, `interval_seconds`, `cooldown_seconds`, `max_buffered_messages`, `classifier_max_tokens`, `classifier_model`, `bot_name_aliases_json`, `classifier_guidelines`)
VALUES
    (1, @enabled, @intervalSeconds, @cooldownSeconds, @maxBufferedMessages, @classifierMaxTokens, @classifierModel, @botNameAliasesJson, @classifierGuidelines)
ON DUPLICATE KEY UPDATE
    `enabled` = @enabled,
    `interval_seconds` = @intervalSeconds,
    `cooldown_seconds` = @cooldownSeconds,
    `max_buffered_messages` = @maxBufferedMessages,
    `classifier_max_tokens` = @classifierMaxTokens,
    `classifier_model` = @classifierModel,
    `bot_name_aliases_json` = @botNameAliasesJson,
    `classifier_guidelines` = @classifierGuidelines,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                enabled,
                intervalSeconds,
                cooldownSeconds,
                maxBufferedMessages,
                classifierMaxTokens,
                classifierModel,
                botNameAliasesJson,
                classifierGuidelines
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask AddAsync(AutoResponseEventInput input, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `auto_response_events`
    (`guild_id`, `channel_id`, `trigger_message_id`, `message_ids_json`, `decision`, `reason`, `focus`, `error_stage`, `http_status_code`, `error_message`)
VALUES
    (@guildId, @channelId, @triggerMessageId, @messageIdsJson, @decision, @reason, @focus, @errorStage, @httpStatusCode, @errorMessage)";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                input.GuildId,
                input.ChannelId,
                input.TriggerMessageId,
                input.MessageIdsJson,
                input.Decision,
                input.Reason,
                input.Focus,
                input.ErrorStage,
                input.HttpStatusCode,
                input.ErrorMessage
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask<IReadOnlyList<AutoResponseEventData>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `trigger_message_id` AS TriggerMessageId,
    `message_ids_json` AS MessageIdsJson,
    `decision` AS Decision,
    `reason` AS Reason,
    `focus` AS Focus,
    `error_stage` AS ErrorStage,
    `http_status_code` AS HttpStatusCode,
    `error_message` AS ErrorMessage,
    `created_at` AS CreatedAt
FROM `auto_response_events`
ORDER BY `created_at` DESC, `id` DESC
LIMIT @limit";

        var command = new CommandDefinition(QUERY, new { limit }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<AutoResponseEventData>(command);
        return results.ToList();
    }
}
