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
    `instructions` AS Instructions,
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
        string instructions,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `claude_settings`
    (`id`, `model`, `summary_model`, `default_max_tokens`, `instructions`)
VALUES
    (1, @model, @summaryModel, @defaultMaxTokens, @instructions)
ON DUPLICATE KEY UPDATE
    `model` = @model,
    `summary_model` = @summaryModel,
    `default_max_tokens` = @defaultMaxTokens,
    `instructions` = @instructions,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { model, summaryModel, defaultMaxTokens, instructions },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
