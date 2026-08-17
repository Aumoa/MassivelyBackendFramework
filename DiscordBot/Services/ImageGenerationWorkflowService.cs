using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

public interface IImageGenerationWorkflowService
{
    ValueTask<string?> GetWorkflowJsonAsync(CancellationToken cancellationToken = default);

    ValueTask SaveWorkflowJsonAsync(string workflowJson, CancellationToken cancellationToken = default);
}

internal sealed class ImageGenerationWorkflowService(IImageGenerationWorkflowRepository repository)
    : IImageGenerationWorkflowService
{
    public async ValueTask<string?> GetWorkflowJsonAsync(CancellationToken cancellationToken = default)
    {
        var data = await repository.GetAsync(cancellationToken);
        return data?.WorkflowJson;
    }

    public async ValueTask SaveWorkflowJsonAsync(string workflowJson, CancellationToken cancellationToken = default)
    {
        var trimmed = workflowJson.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Workflow JSON is required.", nameof(workflowJson));
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(trimmed);
        }
        catch (JsonException e)
        {
            throw new ArgumentException("Workflow JSON is not valid JSON.", nameof(workflowJson), e);
        }

        if (parsed is not JsonObject)
        {
            throw new ArgumentException("Workflow JSON must be a JSON object.", nameof(workflowJson));
        }

        await repository.UpsertAsync(trimmed, cancellationToken);
    }
}
