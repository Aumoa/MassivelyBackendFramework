namespace OpenAI.Services;

internal class OllamaAIMessenger : IAIMessenger
{
    public async ValueTask<string> GenerateConversationTopicsAsync(string message, CancellationToken cancellationToken = default)
    {
        // TODO: Change this to use the OpenAI API to generate conversation topics based on the message.
        if (message.Length > 10)
        {
            return message[..10] + "...";
        }
        else
        {
            return message;
        }
    }

    public ValueTask<IAIChat> CreateChatAsync(string conversationTopics, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAIChat>(new OllamaAIChat(conversationTopics));
    }
}
