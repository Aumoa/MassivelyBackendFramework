using System.Text.Json;
using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlAiSkillRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAiSkillRepository
{
    public async ValueTask<IReadOnlyList<AiSkillData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `name` AS Name,
    `description` AS Description,
    `priority` AS Priority,
    `trigger_phrases_json` AS TriggerPhrasesJson,
    `instructions` AS Instructions,
    `enabled` AS Enabled,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `ai_skill`
ORDER BY `priority` DESC, `name` ASC";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var skills = await connection.QueryAsync<AiSkillData>(command);
        return [.. skills];
    }

    public async ValueTask UpsertAsync(
        string name,
        string description,
        int priority,
        IReadOnlyList<string> triggerPhrases,
        string instructions,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `ai_skill`
    (`name`, `description`, `priority`, `trigger_phrases_json`, `instructions`, `enabled`)
VALUES
    (@name, @description, @priority, @triggerPhrasesJson, @instructions, @enabled)
ON DUPLICATE KEY UPDATE
    `description` = @description,
    `priority` = @priority,
    `trigger_phrases_json` = @triggerPhrasesJson,
    `instructions` = @instructions,
    `enabled` = @enabled,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                name,
                description,
                priority,
                triggerPhrasesJson = JsonSerializer.Serialize(triggerPhrases),
                instructions,
                enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
