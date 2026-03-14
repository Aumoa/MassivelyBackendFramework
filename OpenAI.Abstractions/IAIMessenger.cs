namespace OpenAI;

public interface IAIMessenger
{
    ValueTask<string> GenerateConversationTopicsAsync(string message, CancellationToken cancellationToken = default);

    ValueTask<IAIChat> CreateChatAsync(string conversationTopics, CancellationToken cancellationToken = default);
}
