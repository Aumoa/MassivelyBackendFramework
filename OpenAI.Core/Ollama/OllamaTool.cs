using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OllamaFunction Function { get; init; }
}
