namespace OpenAI;

public record ChatSessionData(string Id, string UserId, string Topic, DateTime CreatedAt);

public record ChatMessageData(long Id, string SessionId, bool IsUser, string Content, DateTime CreatedAt);

public interface IChatRepository
{
    ValueTask<IReadOnlyList<ChatSessionData>> GetSessionsAsync(string userId, CancellationToken cancellationToken = default);

    ValueTask<string> CreateSessionAsync(string userId, string topic, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ChatMessageData>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    ValueTask AddMessageAsync(string sessionId, bool isUser, string content, CancellationToken cancellationToken = default);
}
