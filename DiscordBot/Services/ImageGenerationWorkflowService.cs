using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

public sealed record ImageGenerationWorkflowSummary(
    string Name,
    string? Description,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ImageGenerationWorkflowDetail(
    string Name,
    string? Description,
    string WorkflowJson,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IImageGenerationWorkflowService
{
    ValueTask<IReadOnlyList<ImageGenerationWorkflowSummary>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ImageGenerationWorkflowDetail?> GetAsync(string name, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        string name,
        string? description,
        string workflowJson,
        int sortOrder,
        CancellationToken cancellationToken = default);

    ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default);
}

internal sealed class ImageGenerationWorkflowService(IImageGenerationWorkflowRepository repository)
    : IImageGenerationWorkflowService
{
    public async ValueTask<IReadOnlyList<ImageGenerationWorkflowSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await repository.ListAsync(cancellationToken);
        return rows
            .Select(row => new ImageGenerationWorkflowSummary(row.Name, row.Description, row.SortOrder, row.CreatedAt, row.UpdatedAt))
            .ToList();
    }

    public async ValueTask<ImageGenerationWorkflowDetail?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var row = await repository.GetAsync(name, cancellationToken);
        return row == null
            ? null
            : new ImageGenerationWorkflowDetail(row.Name, row.Description, row.WorkflowJson, row.SortOrder, row.CreatedAt, row.UpdatedAt);
    }

    public async ValueTask SaveAsync(
        string name,
        string? description,
        string workflowJson,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("Workflow name is required.", nameof(name));
        }

        var trimmedJson = workflowJson.Trim();
        if (trimmedJson.Length == 0)
        {
            throw new ArgumentException("Workflow JSON is required.", nameof(workflowJson));
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(trimmedJson);
        }
        catch (JsonException e)
        {
            throw new ArgumentException("Workflow JSON is not valid JSON.", nameof(workflowJson), e);
        }

        if (parsed is not JsonObject)
        {
            throw new ArgumentException("Workflow JSON must be a JSON object.", nameof(workflowJson));
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await repository.UpsertAsync(trimmedName, trimmedDescription, trimmedJson, sortOrder, cancellationToken);
    }

    public async ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var trimmedNewName = newName.Trim();
        if (trimmedNewName.Length == 0)
        {
            throw new ArgumentException("Workflow name is required.", nameof(newName));
        }

        await repository.RenameAsync(oldName, trimmedNewName, cancellationToken);
    }

    public async ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await repository.DeleteAsync(name, cancellationToken);
    }
}
