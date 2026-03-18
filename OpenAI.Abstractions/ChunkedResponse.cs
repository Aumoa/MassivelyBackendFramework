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

    /// <summary>
    /// Optional structured hint for the UI layer. When set, the UI should render
    /// a widget specific to the hint type rather than plain text.
    /// </summary>
    public ToolHint? Hint { get; set; }
}
