namespace DiscordBot.Repositories;

public record ImageGenerationWorkflowData(
    string WorkflowJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IImageGenerationWorkflowRepository
{
    ValueTask<ImageGenerationWorkflowData?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(string workflowJson, CancellationToken cancellationToken = default);
}
