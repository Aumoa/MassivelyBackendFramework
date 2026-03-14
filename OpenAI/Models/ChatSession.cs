namespace OpenAI.Models;

public class ChatSession(IAIChat chat)
{
    public IAIChat Chat { get; } = chat;
    public List<ChatMessage> Messages { get; } = [];
}
