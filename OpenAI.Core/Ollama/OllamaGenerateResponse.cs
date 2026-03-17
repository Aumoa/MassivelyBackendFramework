using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public required string Response { get; init; }
}
