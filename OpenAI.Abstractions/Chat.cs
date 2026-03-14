namespace OpenAI;

public enum MessageRole
{
    User = 0,
    Assistant = 1,
    Summary = 2
}

public record ChatHistoryMessage(MessageRole Role, string Content);
