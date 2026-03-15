using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required OllamaChatMessage[] Messages { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    [JsonPropertyName("options")]
    public OllamaChatOptions? Options { get; init; }
}

internal record OllamaChatOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double TopP { get; init; }

    [JsonPropertyName("num_ctx")]
    public int NumCtx { get; init; }

    [JsonPropertyName("repeat_penalty")]
    public double RepeatPenalty { get; init; }
}
