namespace AI;

public record ChatCompletionOptions
{
    public required string Model { get; init; }

    public float Temperature { get; init; } = 0.7f;

    public float? TopP { get; init; }

    public int? MaxTokens { get; init; }

    public int? ContextLength { get; init; }

    public float? RepeatPenalty { get; init; }
}
