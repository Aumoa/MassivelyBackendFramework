using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.ImageGeneration;

public sealed class ComfyUIClient(
    HttpClient http,
    IOptions<ImageGenerationOptions> options,
    IHostEnvironment environment,
    ILogger<ComfyUIClient> logger) : IImageGenerationClient
{
    private readonly ImageGenerationOptions m_Options = options.Value;
    private readonly SemaphoreSlim m_Semaphore = new(Math.Max(1, options.Value.MaxConcurrentJobs));

    public async Task<GeneratedImage> GenerateAsync(
        string positivePrompt,
        string negativePrompt,
        CancellationToken cancellationToken = default)
    {
        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            var workflow = await LoadWorkflowAsync(cancellationToken);
            var positivePromptNodeId = FindNodeIdByMetaTitle(workflow, m_Options.PositivePromptTitle);
            var negativePromptNodeId = FindNodeIdByMetaTitle(workflow, m_Options.NegativePromptTitle);
            var saveImageNodeId = FindNodeIdByMetaTitle(workflow, m_Options.SaveImageTitle);

            SetWorkflowInput(workflow, positivePromptNodeId, "text", positivePrompt);
            SetWorkflowInput(workflow, negativePromptNodeId, "text", negativePrompt);
            SetWorkflowInput(workflow, saveImageNodeId, "filename_prefix", BuildFilenamePrefix());

            string promptId = await QueuePromptAsync(workflow, cancellationToken);
            var image = await WaitForImageAsync(promptId, saveImageNodeId, cancellationToken);

            var viewPath = BuildViewPath(image);
            var bytes = await http.GetByteArrayAsync(viewPath, cancellationToken);
            return new GeneratedImage(bytes, image.FileName, GetContentType(image.FileName));
        }
        finally
        {
            m_Semaphore.Release();
        }
    }

    private async Task<JsonObject> LoadWorkflowAsync(CancellationToken cancellationToken)
    {
        var path = m_Options.WorkflowPath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(environment.ContentRootPath, path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"ComfyUI workflow file was not found: {path}", path);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"ComfyUI workflow file is not a JSON object: {path}");
    }

    private async Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["client_id"] = m_Options.ClientId,
            ["prompt"] = workflow
        };

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("prompt", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("node_errors", out var nodeErrors)
            && nodeErrors.ValueKind == JsonValueKind.Object
            && nodeErrors.EnumerateObject().Any())
        {
            throw new InvalidOperationException($"ComfyUI rejected the workflow: {nodeErrors}");
        }

        if (!document.RootElement.TryGetProperty("prompt_id", out var promptIdProperty))
        {
            throw new InvalidOperationException($"ComfyUI did not return prompt_id: {responseBody}");
        }

        var promptId = promptIdProperty.GetString();
        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new InvalidOperationException($"ComfyUI returned an empty prompt_id: {responseBody}");
        }

        return promptId;
    }

    private async Task<ComfyUIImage> WaitForImageAsync(
        string promptId,
        string saveImageNodeId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, m_Options.TimeoutSeconds)));

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            using var response = await http.GetAsync($"history/{Uri.EscapeDataString(promptId)}", timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var image = TryFindImage(document.RootElement, promptId, saveImageNodeId);
                if (image != null)
                {
                    return image;
                }
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("ComfyUI prompt {PromptId} is still running.", promptId);
            }

            await Task.Delay(Math.Max(250, m_Options.PollIntervalMilliseconds), timeout.Token);
        }
    }

    private static ComfyUIImage? TryFindImage(JsonElement root, string promptId, string saveImageNodeId)
    {
        if (!root.TryGetProperty(promptId, out var promptHistory))
        {
            return null;
        }

        if (!promptHistory.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryFindImageInOutput(outputs, saveImageNodeId, out var configuredImage))
        {
            return configuredImage;
        }

        foreach (var output in outputs.EnumerateObject())
        {
            if (TryReadFirstImage(output.Value, out var image))
            {
                return image;
            }
        }

        return null;
    }

    private static bool TryFindImageInOutput(JsonElement outputs, string nodeId, out ComfyUIImage image)
    {
        image = default!;
        if (!outputs.TryGetProperty(nodeId, out var output))
        {
            return false;
        }

        return TryReadFirstImage(output, out image);
    }

    private static bool TryReadFirstImage(JsonElement output, out ComfyUIImage image)
    {
        image = default!;
        if (!output.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in images.EnumerateArray())
        {
            var fileName = item.TryGetProperty("filename", out var filenameProperty)
                ? filenameProperty.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            image = new ComfyUIImage(
                fileName,
                item.TryGetProperty("subfolder", out var subfolderProperty) ? subfolderProperty.GetString() ?? "" : "",
                item.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() ?? "output" : "output");
            return true;
        }

        return false;
    }

    private static void SetWorkflowInput(JsonObject workflow, string nodeId, string inputName, string value)
    {
        if (workflow[nodeId] is not JsonObject node)
        {
            throw new InvalidOperationException($"Workflow node {nodeId} does not exist.");
        }

        if (node["inputs"] is not JsonObject inputs)
        {
            throw new InvalidOperationException($"Workflow node {nodeId} does not have inputs.");
        }

        inputs[inputName] = value;
    }

    private static string FindNodeIdByMetaTitle(JsonObject workflow, string title)
    {
        foreach (var (nodeId, nodeValue) in workflow)
        {
            if (nodeValue is not JsonObject node)
            {
                continue;
            }

            if (node["_meta"] is not JsonObject meta)
            {
                continue;
            }

            var nodeTitle = meta["title"]?.GetValue<string>();
            if (string.Equals(nodeTitle, title, StringComparison.Ordinal))
            {
                return nodeId;
            }
        }

        throw new InvalidOperationException($"Workflow node with _meta.title '{title}' does not exist.");
    }

    private string BuildFilenamePrefix()
    {
        var safePrefix = string.IsNullOrWhiteSpace(m_Options.FilenamePrefix)
            ? "DiscordBot"
            : m_Options.FilenamePrefix.Trim();
        return $"{safePrefix}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
    }

    private static string BuildViewPath(ComfyUIImage image)
    {
        var path = "view"
            + $"?filename={Uri.EscapeDataString(image.FileName)}"
            + $"&subfolder={Uri.EscapeDataString(image.Subfolder)}"
            + $"&type={Uri.EscapeDataString(image.Type)}";
        return path;
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : "image/png";
    }

    private sealed record ComfyUIImage(string FileName, string Subfolder, string Type);
}
