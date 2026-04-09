namespace AI;

public record ChatMessage
{
    public required ChatRole Role { get; init; }

    public string Content { get; init; } = "";

    public IReadOnlyList<string>? Images { get; init; }

    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    public string? ToolCallId { get; init; }
}
