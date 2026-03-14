using System.Runtime.CompilerServices;

namespace OpenAI.Services;

internal class OllamaAIChat(string conversationTopics) : IAIChat
{
    public string ConversationTopics { get; } = conversationTopics;

    public async IAsyncEnumerable<string> AddChatAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO: Change this to use the OpenAI API to generate a response based on the message and conversation topics.
        const int TEMP_Chunk = 3;
        for (var i = 0; i < message.Length; i += TEMP_Chunk)
        {
            await Task.Delay(200, cancellationToken);
            yield return message.Substring(i, Math.Min(TEMP_Chunk, message.Length - i));
        }
    }
}
