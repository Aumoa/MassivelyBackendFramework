using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaProperty
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // e.g., "string", "number", "integer", "boolean"

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Enum { get; init; }
}
