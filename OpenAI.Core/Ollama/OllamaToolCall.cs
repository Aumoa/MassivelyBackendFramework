using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("function")]
    public required OllamaFunctionCall Function { get; init; }
}
