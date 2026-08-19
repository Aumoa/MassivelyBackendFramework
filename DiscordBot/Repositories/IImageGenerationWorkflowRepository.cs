namespace DiscordBot.Repositories;

public record ImageGenerationWorkflowData(
    string Name,
    string? Description,
    string WorkflowJson,
    int SortOrder,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IImageGenerationWorkflowRepository
{
    ValueTask<IReadOnlyList<ImageGenerationWorkflowData>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ImageGenerationWorkflowData?> GetAsync(string name, CancellationToken cancellationToken = default);

    ValueTask<ImageGenerationWorkflowData?> GetFirstAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        string name,
        string? description,
        string workflowJson,
        int sortOrder,
        CancellationToken cancellationToken = default);

    ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default);
}
