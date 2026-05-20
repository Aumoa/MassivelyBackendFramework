using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AI.Providers.Ollama;

public class OllamaChatClient(HttpClient http, IOptions<OllamaChatClientOptions> options) : IChatClient
{
    private readonly OllamaChatClientOptions m_Options = options.Value;

    public async IAsyncEnumerable<ChatResponseChunk> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        IReadOnlyList<ToolFunctionDescription>? tools = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ollamaMessages = messages.Select(ToOllamaMessage).ToArray();

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = ollamaMessages,
            ["stream"] = true,
            ["keep_alive"] = m_Options.KeepAlive,
            ["options"] = BuildOptions(options)
        };

        if (tools is { Count: > 0 })
        {
            requestBody["tools"] = CreateOllamaTools(tools);
        }

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, m_Options.Uri + "/api/chat")
        {
            Content = content
        };

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponseDto>(line);
            if (chunk == null) continue;

            List<ChatToolCall>? toolCalls = null;
            if (chunk.Message.ToolCalls is { Length: > 0 })
            {
                toolCalls = chunk.Message.ToolCalls.Select(tc => new ChatToolCall
                {
                    Id = tc.Id,
                    FunctionName = tc.Function.Name,
                    Arguments = tc.Function.Arguments
                }).ToList();
            }

            if (!string.IsNullOrEmpty(chunk.Message.Content) ||
                !string.IsNullOrEmpty(chunk.Message.Thinking) ||
                toolCalls != null)
            {
                yield return new ChatResponseChunk
                {
                    Content = chunk.Message.Content,
                    Thinking = chunk.Message.Thinking,
                    ToolCalls = toolCalls
                };
            }

            if (chunk.Done) break;
        }
    }

    public async Task<string> GenerateAsync(
        string prompt,
        ChatCompletionOptions options,
        string? system = null,
        CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync(m_Options.Uri + "/api/generate", new
        {
            model = options.Model,
            prompt,
            system,
            stream = false,
            keep_alive = m_Options.KeepAlive,
            options = BuildOptions(options)
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponseDto>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize Ollama generate response.");

        return result.Response;
    }

    private static OllamaMessageDto ToOllamaMessage(ChatMessage message) => new()
    {
        Role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        },
        Content = message.Content,
        Images = message.Images?.Count > 0 ? [.. message.Images.Select(i => i.Base64)] : null,
        ToolCalls = message.ToolCalls?.Select(tc => new OllamaToolCallDto
        {
            Id = tc.Id,
            Function = new OllamaFunctionDto
            {
                Name = tc.FunctionName,
                Arguments = tc.Arguments
            }
        }).ToArray(),
        ToolCallId = message.ToolCallId
    };

    private static Dictionary<string, object> BuildOptions(ChatCompletionOptions options)
    {
        var dict = new Dictionary<string, object>
        {
            ["temperature"] = options.Temperature
        };

        if (options.TopP.HasValue) dict["top_p"] = options.TopP.Value;
        if (options.ContextLength.HasValue) dict["num_ctx"] = options.ContextLength.Value;
        if (options.MaxTokens.HasValue) dict["num_predict"] = options.MaxTokens.Value;
        if (options.RepeatPenalty.HasValue) dict["repeat_penalty"] = options.RepeatPenalty.Value;

        return dict;
    }

    private static object[] CreateOllamaTools(IReadOnlyList<ToolFunctionDescription> tools) =>
        [.. tools.Select(f => new
        {
            type = "function",
            function = new
            {
                name = f.Name,
                description = f.Description,
                parameters = new
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
            }
        })];

    #region Internal Ollama DTOs

    private record OllamaMessageDto
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public string Content { get; init; } = "";

        [JsonPropertyName("thinking")]
        public string Thinking { get; init; } = "";

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Images { get; init; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OllamaToolCallDto[]? ToolCalls { get; init; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; init; }
    }

    private record OllamaToolCallDto
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("function")]
        public required OllamaFunctionDto Function { get; init; }
    }

    private record OllamaFunctionDto
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("arguments")]
        public required JsonElement Arguments { get; init; }
    }

    private record OllamaChatResponseDto
    {
        [JsonPropertyName("message")]
        public required OllamaMessageDto Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }
    }

    private record OllamaGenerateResponseDto
    {
        [JsonPropertyName("response")]
        public required string Response { get; init; }
    }

    #endregion
}
