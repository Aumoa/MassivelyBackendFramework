namespace OpenAI;

public record AIModelOptions
{
    public required string GenerateTopicsModel { get; init; }

    public required string ChatModel { get; init; }
}
