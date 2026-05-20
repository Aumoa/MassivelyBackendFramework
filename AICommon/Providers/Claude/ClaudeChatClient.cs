using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AI.Providers.Claude;

public class ClaudeChatClient(HttpClient http, IOptions<ClaudeChatClientOptions> options) : IChatClient
{
    private readonly ClaudeChatClientOptions m_Options = options.Value;

    public async IAsyncEnumerable<ChatResponseChunk> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions completionOptions,
        IReadOnlyList<ToolFunctionDescription>? tools = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (system, claudeMessages) = BuildMessages(messages);

        var requestBody = BuildRequestBody(
            model: completionOptions.Model,
            maxTokens: completionOptions.MaxTokens ?? m_Options.DefaultMaxTokens,
            system: system,
            messages: claudeMessages,
            tools: tools,
            completionOptions: completionOptions,
            stream: true);

        using var request = CreateRequest(requestBody);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Accumulate partial JSON for tool_use input across input_json_delta events
        Dictionary<int, ToolUseAccumulator> toolAccumulators = [];

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:")) continue;

            var data = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(data)) continue;

            JsonElement evt;
            try
            {
                evt = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!evt.TryGetProperty("type", out var typeProp)) continue;
            var eventType = typeProp.GetString();

            switch (eventType)
            {
                case "content_block_start":
                {
                    if (!evt.TryGetProperty("index", out var idxProp)) break;
                    int index = idxProp.GetInt32();
                    if (!evt.TryGetProperty("content_block", out var blockProp)) break;
                    if (!blockProp.TryGetProperty("type", out var blockTypeProp)) break;

                    if (blockTypeProp.GetString() == "tool_use")
                    {
                        toolAccumulators[index] = new ToolUseAccumulator
                        {
                            Id = blockProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = blockProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            JsonBuffer = new StringBuilder()
                        };
                    }
                    break;
                }
                case "content_block_delta":
                {
                    if (!evt.TryGetProperty("delta", out var deltaProp)) break;
                    if (!deltaProp.TryGetProperty("type", out var deltaTypeProp)) break;

                    var deltaType = deltaTypeProp.GetString();
                    switch (deltaType)
                    {
                        case "text_delta":
                        {
                            var text = deltaProp.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return new ChatResponseChunk { Content = text };
                            }
                            break;
                        }
                        case "thinking_delta":
                        {
                            var thinking = deltaProp.TryGetProperty("thinking", out var t) ? t.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(thinking))
                            {
                                yield return new ChatResponseChunk { Thinking = thinking };
                            }
                            break;
                        }
                        case "input_json_delta":
                        {
                            if (!evt.TryGetProperty("index", out var idxProp)) break;
                            int index = idxProp.GetInt32();
                            if (!toolAccumulators.TryGetValue(index, out var acc)) break;
                            var partial = deltaProp.TryGetProperty("partial_json", out var p) ? p.GetString() ?? "" : "";
                            acc.JsonBuffer.Append(partial);
                            break;
                        }
                    }
                    break;
                }
                case "content_block_stop":
                {
                    if (!evt.TryGetProperty("index", out var idxProp)) break;
                    int index = idxProp.GetInt32();
                    if (!toolAccumulators.TryGetValue(index, out var acc)) break;

                    JsonElement args;
                    var jsonText = acc.JsonBuffer.ToString();
                    if (string.IsNullOrWhiteSpace(jsonText))
                    {
                        args = JsonSerializer.Deserialize<JsonElement>("{}");
                    }
                    else
                    {
                        try
                        {
                            args = JsonSerializer.Deserialize<JsonElement>(jsonText);
                        }
                        catch (JsonException)
                        {
                            args = JsonSerializer.Deserialize<JsonElement>("{}");
                        }
                    }

                    yield return new ChatResponseChunk
                    {
                        ToolCalls =
                        [
                            new ChatToolCall
                            {
                                Id = acc.Id,
                                FunctionName = acc.Name,
                                Arguments = args
                            }
                        ]
                    };

                    toolAccumulators.Remove(index);
                    break;
                }
                case "message_stop":
                    yield break;
            }
        }
    }

    public async Task<string> GenerateAsync(
        string prompt,
        ChatCompletionOptions completionOptions,
        string? system = null,
        CancellationToken cancellationToken = default)
    {
        var claudeMessages = new[]
        {
            new
            {
                role = "user",
                content = new object[] { new { type = "text", text = prompt } }
            }
        };

        var requestBody = BuildRequestBody(
            model: completionOptions.Model,
            maxTokens: completionOptions.MaxTokens ?? m_Options.DefaultMaxTokens,
            system: system,
            messages: claudeMessages,
            tools: null,
            completionOptions: completionOptions,
            stream: false);

        using var request = CreateRequest(requestBody);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var contentArray))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                && block.TryGetProperty("text", out var text))
            {
                sb.Append(text.GetString());
            }
        }
        return sb.ToString();
    }

    private HttpRequestMessage CreateRequest(string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, m_Options.BaseUri.TrimEnd('/') + "/v1/messages")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", m_Options.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", m_Options.AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
        }
        throw new HttpRequestException($"Claude API {(int)response.StatusCode} {response.StatusCode}: {body}");
    }

    private string BuildRequestBody(
        string model,
        int maxTokens,
        string? system,
        object messages,
        IReadOnlyList<ToolFunctionDescription>? tools,
        ChatCompletionOptions completionOptions,
        bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["stream"] = stream,
            ["messages"] = JsonSerializer.SerializeToNode(messages)
        };

        if (!string.IsNullOrEmpty(system))
        {
            body["system"] = system;
        }

        // Opus 4.7 rejects temperature/top_p/top_k — skip for any opus model.
        bool isOpus = model.Contains("opus", StringComparison.OrdinalIgnoreCase);
        if (!isOpus)
        {
            body["temperature"] = completionOptions.Temperature;
            if (completionOptions.TopP.HasValue)
            {
                body["top_p"] = completionOptions.TopP.Value;
            }
        }

        if (tools is { Count: > 0 })
        {
            body["tools"] = JsonSerializer.SerializeToNode(CreateClaudeTools(tools));
        }

        return body.ToJsonString();
    }

    private static (string? System, object[] Messages) BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        string? system = null;
        List<object> claudeMessages = [];
        List<object>? pendingUserContent = null;

        void FlushPendingUser()
        {
            if (pendingUserContent is { Count: > 0 })
            {
                claudeMessages.Add(new { role = "user", content = pendingUserContent.ToArray() });
                pendingUserContent = null;
            }
        }

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case ChatRole.System:
                    // Concatenate multiple system messages.
                    system = string.IsNullOrEmpty(system) ? msg.Content : system + "\n\n" + msg.Content;
                    break;

                case ChatRole.User:
                {
                    FlushPendingUser();
                    var content = new List<object>();
                    if (msg.Images is { Count: > 0 })
                    {
                        foreach (var image in msg.Images)
                        {
                            content.Add(BuildImageBlock(image.Base64, image.MediaType));
                        }
                    }
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        content.Add(new { type = "text", text = msg.Content });
                    }
                    if (content.Count == 0)
                    {
                        content.Add(new { type = "text", text = "" });
                    }
                    claudeMessages.Add(new { role = "user", content = content.ToArray() });
                    break;
                }

                case ChatRole.Assistant:
                {
                    FlushPendingUser();
                    var content = new List<object>();
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        content.Add(new { type = "text", text = msg.Content });
                    }
                    if (msg.ToolCalls is { Count: > 0 })
                    {
                        foreach (var call in msg.ToolCalls)
                        {
                            content.Add(new
                            {
                                type = "tool_use",
                                id = call.Id,
                                name = call.FunctionName,
                                input = call.Arguments
                            });
                        }
                    }
                    if (content.Count == 0)
                    {
                        content.Add(new { type = "text", text = "" });
                    }
                    claudeMessages.Add(new { role = "assistant", content = content.ToArray() });
                    break;
                }

                case ChatRole.Tool:
                {
                    // Tool results must be delivered as a user message in Claude's API.
                    pendingUserContent ??= [];
                    pendingUserContent.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = msg.ToolCallId ?? "",
                        content = msg.Content ?? ""
                    });
                    break;
                }
            }
        }

        FlushPendingUser();
        return (system, claudeMessages.ToArray());
    }

    private static object BuildImageBlock(string base64Data, string? mediaType)
    {
        return new
        {
            type = "image",
            source = new
            {
                type = "base64",
                media_type = string.IsNullOrEmpty(mediaType) ? DetectMediaType(base64Data) : mediaType,
                data = base64Data
            }
        };
    }

    private static string DetectMediaType(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "image/png";
        try
        {
            Span<byte> header = stackalloc byte[12];
            int written = Convert.TryFromBase64Chars(base64.AsSpan(0, Math.Min(base64.Length, 16)), header, out var bytesWritten)
                ? bytesWritten
                : 0;
            if (written >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "image/jpeg";
            if (written >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return "image/png";
            if (written >= 6 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46) return "image/gif";
            if (written >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46) return "image/webp";
        }
        catch (FormatException)
        {
        }
        return "image/png";
    }

    private static object[] CreateClaudeTools(IReadOnlyList<ToolFunctionDescription> tools) =>
        [.. tools.Select(f => new
        {
            name = f.Name,
            description = f.Description,
            input_schema = new
            {
                type = "object",
                properties = f.Parameters.ToDictionary(
                    p => p.Name,
                    p => (object)new
                    {
                        type = p.Type switch
                        {
                            ToolFunctionDescription.SimpleType.String => "string",
                            ToolFunctionDescription.SimpleType.Number => "number",
                            ToolFunctionDescription.SimpleType.Integer => "integer",
                            ToolFunctionDescription.SimpleType.Boolean => "boolean",
                            _ => "string"
                        },
                        description = p.Description
                    }),
                required = f.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray()
            }
        })];

    private sealed class ToolUseAccumulator
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required StringBuilder JsonBuffer { get; init; }
    }
}
