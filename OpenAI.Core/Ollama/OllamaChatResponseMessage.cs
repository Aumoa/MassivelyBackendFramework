using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaChatResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; init; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    public OllamaToolCall[]? ToolCalls { get; init; }
}
