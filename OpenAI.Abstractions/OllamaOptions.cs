namespace OpenAI;

public record OllamaOptions
{
    public required string Uri { get; init; }

    public required string GenerateTopicsModel { get; init; }

    public required string ChatModel { get; init; }
}
