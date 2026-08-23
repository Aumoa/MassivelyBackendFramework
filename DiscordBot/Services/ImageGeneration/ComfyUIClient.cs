using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.ImageGeneration;

public sealed class ComfyUIClient(
    HttpClient http,
    IOptions<ImageGenerationOptions> options,
    IImageGenerationWorkflowRepository workflowRepository,
    ILogger<ComfyUIClient> logger) : IImageGenerationClient
{
    private readonly ImageGenerationOptions m_Options = options.Value;
    private readonly SemaphoreSlim m_Semaphore = new(Math.Max(1, options.Value.MaxConcurrentJobs));

    public async Task<GeneratedImage> GenerateAsync(
        string positivePrompt,
        IProgress<ImageGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await m_Semaphore.WaitAsync(cancellationToken);
        ClientWebSocket? progressSocket = null;
        Task? progressTask = null;
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var workflow = await LoadWorkflowAsync(cancellationToken);
            RandomizeSeeds(workflow, () => Random.Shared.NextInt64(0, long.MaxValue));

            var positivePromptNodeId = FindNodeIdByMetaTitle(workflow, m_Options.PositivePromptTitle);
            var saveImageNodeId = FindNodeIdByMetaTitle(workflow, m_Options.SaveImageTitle);
            var nodeTitles = BuildNodeTitleMap(workflow);

            // The negative prompt stays fixed as whatever is already baked into the workflow JSON;
            // only the positive prompt node is overwritten per request.
            SetWorkflowInput(workflow, positivePromptNodeId, "text", positivePrompt);
            SetWorkflowInput(workflow, saveImageNodeId, "filename_prefix", BuildFilenamePrefix());

            var clientId = BuildClientId();
            progressSocket = await TryConnectProgressSocketAsync(clientId, progress, cancellationToken);

            string promptId = await QueuePromptAsync(workflow, clientId, cancellationToken);
            if (progressSocket != null && progress != null)
            {
                progressTask = ListenForProgressAsync(
                    progressSocket,
                    promptId,
                    nodeTitles,
                    progress,
                    progressCts.Token);
            }

            var image = await WaitForImageAsync(promptId, saveImageNodeId, cancellationToken);

            var viewPath = BuildViewPath(image);
            var bytes = await http.GetByteArrayAsync(viewPath, cancellationToken);
            return new GeneratedImage(bytes, image.FileName, GetContentType(image.FileName));
        }
        finally
        {
            await StopProgressSocketAsync(progressSocket, progressCts, progressTask);
            m_Semaphore.Release();
        }
    }

    // 워크플로우 작성자가 시드 노드에 별도 제목을 붙이는 경우는 드물어서 _meta.title이 아닌
    // 입력 필드 이름(KSampler/KSamplerAdvanced/RandomNoise 계열이 공통으로 쓰는 이름)으로 찾는다.
    // 링크(다른 노드 출력에 연결된 입력)는 JsonArray로 표현되므로 JsonValue 체크가 자연스럽게 걸러낸다.
    internal static void RandomizeSeeds(JsonObject workflow, Func<long> nextSeed)
    {
        string[] seedFieldNames = ["seed", "noise_seed"];

        foreach (var (_, nodeValue) in workflow)
        {
            if (nodeValue is not JsonObject node || node["inputs"] is not JsonObject inputs)
            {
                continue;
            }

            foreach (var fieldName in seedFieldNames)
            {
                if (inputs[fieldName] is JsonValue)
                {
                    inputs[fieldName] = nextSeed();
                }
            }
        }
    }

    private async Task<JsonObject> LoadWorkflowAsync(CancellationToken cancellationToken)
    {
        // Multiple workflows can be registered, but concept-based selection isn't implemented yet;
        // always run the first one (by sort_order, then name).
        var workflow = await workflowRepository.GetFirstAsync(cancellationToken);
        if (workflow == null)
        {
            throw new InvalidOperationException(
                "No ComfyUI workflow is configured. Add one on the image generation workflow admin page.");
        }

        return JsonNode.Parse(workflow.WorkflowJson)?.AsObject()
            ?? throw new InvalidOperationException("Stored ComfyUI workflow is not a JSON object.");
    }

    private async Task<string> QueuePromptAsync(JsonObject workflow, string clientId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["client_id"] = clientId,
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

    private async Task<ClientWebSocket?> TryConnectProgressSocketAsync(
        string clientId,
        IProgress<ImageGenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress == null)
        {
            return null;
        }

        try
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(BuildWebSocketUri(clientId), cancellationToken);
            return socket;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to connect to ComfyUI progress websocket.");
            progress.Report(new ImageGenerationProgress("진행 정보 연결 실패"));
            return null;
        }
    }

    private async Task ListenForProgressAsync(
        ClientWebSocket socket,
        string promptId,
        IReadOnlyDictionary<string, string> nodeTitles,
        IProgress<ImageGenerationProgress> progress,
        CancellationToken cancellationToken)
    {
        string? currentNodeTitle = null;

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextMessageAsync(socket, cancellationToken);
                if (message == null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeProperty))
                {
                    continue;
                }

                var type = typeProperty.GetString();
                if (!root.TryGetProperty("data", out var data))
                {
                    continue;
                }

                if (!IsPromptMessage(data, promptId))
                {
                    continue;
                }

                switch (type)
                {
                    case "execution_start":
                        progress.Report(new ImageGenerationProgress("실행 시작"));
                        break;
                    case "executing":
                    {
                        var nodeId = TryGetString(data, "node");
                        currentNodeTitle = nodeId != null && nodeTitles.TryGetValue(nodeId, out var title)
                            ? title
                            : nodeId;

                        progress.Report(new ImageGenerationProgress(
                            string.IsNullOrEmpty(currentNodeTitle) ? "마무리 중" : "노드 실행 중",
                            currentNodeTitle));
                        break;
                    }
                    case "progress":
                    {
                        var value = TryGetInt32(data, "value");
                        var max = TryGetInt32(data, "max");
                        progress.Report(new ImageGenerationProgress("샘플링 중", currentNodeTitle, value, max));
                        break;
                    }
                    case "executed":
                    {
                        var nodeId = TryGetString(data, "node");
                        var nodeTitle = nodeId != null && nodeTitles.TryGetValue(nodeId, out var title)
                            ? title
                            : nodeId;
                        progress.Report(new ImageGenerationProgress("노드 완료", nodeTitle ?? currentNodeTitle));
                        break;
                    }
                    case "execution_error":
                    {
                        var errorMessage = TryGetString(data, "exception_message");
                        progress.Report(new ImageGenerationProgress(
                            string.IsNullOrWhiteSpace(errorMessage) ? "실행 오류" : errorMessage));
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to receive ComfyUI progress websocket message.");
        }
    }

    private static async Task<string?> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                stream.Write(buffer, 0, result.Count);
            }
        }
        while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? Encoding.UTF8.GetString(stream.ToArray())
            : string.Empty;
    }

    private static bool IsPromptMessage(JsonElement data, string promptId)
    {
        var messagePromptId = TryGetString(data, "prompt_id");
        return string.IsNullOrEmpty(messagePromptId)
            || string.Equals(messagePromptId, promptId, StringComparison.Ordinal);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static async Task StopProgressSocketAsync(
        ClientWebSocket? socket,
        CancellationTokenSource progressCts,
        Task? progressTask)
    {
        await progressCts.CancelAsync();

        if (socket != null)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
            }
            catch
            {
            }
        }

        if (progressTask != null)
        {
            try
            {
                await progressTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        socket?.Dispose();
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

    private static IReadOnlyDictionary<string, string> BuildNodeTitleMap(JsonObject workflow)
    {
        Dictionary<string, string> titles = [];
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

            var title = meta["title"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(title))
            {
                titles[nodeId] = title;
            }
        }

        return titles;
    }

    private string BuildFilenamePrefix()
    {
        var safePrefix = string.IsNullOrWhiteSpace(m_Options.FilenamePrefix)
            ? "DiscordBot"
            : m_Options.FilenamePrefix.Trim();
        return $"{safePrefix}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
    }

    private string BuildClientId()
    {
        var prefix = string.IsNullOrWhiteSpace(m_Options.ClientId)
            ? "discordbot"
            : m_Options.ClientId.Trim();
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private Uri BuildWebSocketUri(string clientId)
    {
        var baseAddress = http.BaseAddress
            ?? throw new InvalidOperationException("ComfyUI HttpClient BaseAddress is not configured.");

        var builder = new UriBuilder(baseAddress)
        {
            Scheme = baseAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Query = $"clientId={Uri.EscapeDataString(clientId)}"
        };

        var path = builder.Path.TrimEnd('/');
        builder.Path = string.IsNullOrEmpty(path) ? "ws" : path + "/ws";
        return builder.Uri;
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
