namespace OpenAI;

public record ClaudeOptions
{
    public required string GenerateTopicsModel { get; init; }

    public required string ChatModel { get; init; }
}
