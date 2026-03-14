using System.Runtime.CompilerServices;

namespace OpenAI;

public interface IAIChat
{
    string ConversationTopics { get; }

    IAsyncEnumerable<string> AddChatAsync(string message, CancellationToken cancellationToken = default);
}
