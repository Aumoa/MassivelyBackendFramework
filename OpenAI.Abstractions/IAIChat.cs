namespace OpenAI;

public interface IAIChat
{
    string ConversationTopics { get; }

    IAsyncEnumerable<string> AddChatAsync(IReadOnlyList<ChatHistoryMessage> history, string message, CancellationToken cancellationToken = default);

    ValueTask<string> SummarizeAsync(IReadOnlyList<ChatHistoryMessage> messages, CancellationToken cancellationToken = default);
}
