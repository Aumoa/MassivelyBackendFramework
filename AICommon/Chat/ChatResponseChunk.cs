namespace AI;

public record ChatResponseChunk
{
    public string Content { get; init; } = "";

    public string Thinking { get; init; } = "";

    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }
}
