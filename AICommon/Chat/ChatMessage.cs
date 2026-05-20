namespace AI;

public record ChatMessage
{
    public required ChatRole Role { get; init; }

    public string Content { get; init; } = "";

    public IReadOnlyList<ChatImage>? Images { get; init; }

    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    public string? ToolCallId { get; init; }
}

public record ChatImage
{
    public required string Base64 { get; init; }

    public string? MediaType { get; init; }
}
