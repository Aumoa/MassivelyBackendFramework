using System.Text.Json.Serialization;
using AI;

namespace OpenAI.Ollama;

internal record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required OllamaChatMessage[] Messages { get; init; }

    [JsonPropertyName("tools")]
    public OllamaTool[]? Tools { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    [JsonPropertyName("options")]
    public OllamaChatOptions? Options { get; init; }

    public static OllamaTool[] CreateFrom(IReadOnlyCollection<ToolFunctionDescription> toolFunctions)
    {
        return [.. toolFunctions.Select(tf => new OllamaTool
        {
            Type = "function",
            Function = new OllamaFunction
            {
                Name = tf.Name,
                Description = tf.Description,
                Parameters = OllamaFaceParameters.CreateFrom(tf.Parameters)
            }
        })];
    }
}
