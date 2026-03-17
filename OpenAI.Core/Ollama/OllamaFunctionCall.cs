using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}
