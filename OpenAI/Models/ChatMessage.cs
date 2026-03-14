namespace OpenAI.Models;

public class ChatMessage
{
    public MessageRole Role { get; init; }
    public string Content { get; set; } = string.Empty;
    public long? DbId { get; set; }

    public bool IsUser => Role == MessageRole.User;
}
