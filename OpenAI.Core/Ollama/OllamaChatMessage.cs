using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public OllamaToolCall[]? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}
