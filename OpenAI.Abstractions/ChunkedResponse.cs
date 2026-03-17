namespace OpenAI;

public record ChunkedResponse
{
    public enum Types
    {
        Message,
        ToolContent,
        ToolResult
    }

    public required Types Type { get; set; }

    public string? Content { get; set; }
}
