namespace DiscordBot.Services;

public record ChatResponseChunk
{
    public required string Content { get; set; }

    public required string Thinking { get; set; }

    public string? ToolName { get; set; }
}
