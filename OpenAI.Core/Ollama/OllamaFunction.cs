using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required OllamaFaceParameters Parameters { get; init; }
}
