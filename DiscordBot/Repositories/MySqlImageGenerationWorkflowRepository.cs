using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlImageGenerationWorkflowRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IImageGenerationWorkflowRepository
{
    public async ValueTask<ImageGenerationWorkflowData?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `workflow_json` AS WorkflowJson,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `image_generation_workflows`
WHERE `id` = 1";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ImageGenerationWorkflowData>(command);
    }

    public async ValueTask UpsertAsync(string workflowJson, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `image_generation_workflows`
    (`id`, `workflow_json`)
VALUES
    (1, @workflowJson)
ON DUPLICATE KEY UPDATE
    `workflow_json` = @workflowJson,
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { workflowJson },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
