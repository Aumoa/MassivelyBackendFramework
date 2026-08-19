using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlImageGenerationWorkflowRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IImageGenerationWorkflowRepository
{
    private const string SELECT_COLUMNS = @"
    `name` AS Name,
    `description` AS Description,
    `workflow_json` AS WorkflowJson,
    `sort_order` AS SortOrder,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt";

    public async ValueTask<IReadOnlyList<ImageGenerationWorkflowData>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var query = $@"
SELECT{SELECT_COLUMNS}
FROM `image_generation_workflows`
ORDER BY `sort_order` ASC, `name` ASC";

        var command = new CommandDefinition(query, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<ImageGenerationWorkflowData>(command);
        return rows.ToList();
    }

    public async ValueTask<ImageGenerationWorkflowData?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var query = $@"
SELECT{SELECT_COLUMNS}
FROM `image_generation_workflows`
WHERE `name` = @name";

        var command = new CommandDefinition(query, new { name }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ImageGenerationWorkflowData>(command);
    }

    public async ValueTask<ImageGenerationWorkflowData?> GetFirstAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var query = $@"
SELECT{SELECT_COLUMNS}
FROM `image_generation_workflows`
ORDER BY `sort_order` ASC, `name` ASC
LIMIT 1";

        var command = new CommandDefinition(query, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ImageGenerationWorkflowData>(command);
    }

    public async ValueTask UpsertAsync(
        string name,
        string? description,
        string workflowJson,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `image_generation_workflows`
    (`name`, `description`, `workflow_json`, `sort_order`)
VALUES
    (@name, @description, @workflowJson, @sortOrder)
ON DUPLICATE KEY UPDATE
    `description` = VALUES(`description`),
    `workflow_json` = VALUES(`workflow_json`),
    `sort_order` = VALUES(`sort_order`),
    `updated_at` = NOW()";

        var command = new CommandDefinition(
            QUERY,
            new { name, description, workflowJson, sortOrder },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `image_generation_workflows`
SET `name` = @newName, `updated_at` = NOW()
WHERE `name` = @oldName";

        var command = new CommandDefinition(
            QUERY,
            new { oldName, newName },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"DELETE FROM `image_generation_workflows` WHERE `name` = @name";

        var command = new CommandDefinition(QUERY, new { name }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
