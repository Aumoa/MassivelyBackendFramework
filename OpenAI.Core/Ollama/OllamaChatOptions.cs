using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

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
