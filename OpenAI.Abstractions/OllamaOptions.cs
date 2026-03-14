namespace OpenAI;

public record OllamaOptions
{
    public required string Uri { get; set; }

    public required string GenerateTopicsModel { get; set; }

    public required string ChatModel { get; set; }
}
