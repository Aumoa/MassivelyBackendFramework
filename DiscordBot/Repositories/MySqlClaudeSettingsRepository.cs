using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlClaudeSettingsRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IClaudeSettingsRepository
{
    public async ValueTask<ClaudeSettingsData?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `model` AS Model,
    `summary_model` AS SummaryModel,
    `default_max_tokens` AS DefaultMaxTokens,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `claude_settings`
WHERE `id` = 1";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ClaudeSettingsData>(command);
    }

    public async ValueTask UpsertAsync(
        string model,
        string summaryModel,
        int defaultMaxTokens,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `claude_settings`
    (`id`, `model`, `summary_model`, `default_max_tokens`)
VALUES
    (1, @model, @summaryModel, @defaultMaxTokens)
ON DUPLICATE KEY UPDATE
    `model` = @model,
    `summary_model` = @summaryModel,
    `default_max_tokens` = @defaultMaxTokens,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { model, summaryModel, defaultMaxTokens },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
