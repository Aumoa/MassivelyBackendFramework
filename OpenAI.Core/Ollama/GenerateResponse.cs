using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record GenerateResponse
{
    [JsonPropertyName("response")]
    public required string Response { get; set; }
}
