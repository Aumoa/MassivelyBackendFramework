namespace OpenAI.Models;

public class ChatSession(string sessionId, IAIChat chat)
{
    public string SessionId { get; } = sessionId;
    public IAIChat Chat { get; } = chat;
    public List<ChatMessage> Messages { get; } = [];
}
