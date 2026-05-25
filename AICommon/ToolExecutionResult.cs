namespace AI;

public record ToolExecutionResult
{
    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<ChatImage>? Images { get; init; }

    public static ToolExecutionResult FromText(string? content) => new()
    {
        Content = content ?? string.Empty
    };
}
