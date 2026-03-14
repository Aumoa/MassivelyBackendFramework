namespace OpenAI.Models;

public class ChatMessage
{
    public bool IsUser { get; init; }
    public string Content { get; set; } = string.Empty;
}
