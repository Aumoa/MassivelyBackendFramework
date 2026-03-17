using System.Text.Json.Serialization;

namespace OpenAI.Ollama;

internal record OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatResponseMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }
}
