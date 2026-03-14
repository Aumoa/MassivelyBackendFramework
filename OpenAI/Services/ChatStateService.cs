using OpenAI.Models;

namespace OpenAI.Services;

public class ChatStateService(IChatRepository chatRepository, IAIMessenger aiMessenger)
{
    private readonly List<ChatSession> _sessions = [];
    private bool _initialized;

    public IReadOnlyList<ChatSession> Sessions => _sessions;
    public ChatSession? CurrentSession { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        _initialized = true;

        var dbSessions = await chatRepository.GetSessionsAsync(userId, cancellationToken);
        foreach (var dbSession in dbSessions)
        {
            var dbMessages = await chatRepository.GetMessagesAsync(dbSession.Id, cancellationToken);
            var chat = await aiMessenger.CreateChatAsync(dbSession.Topic, cancellationToken);
            var session = new ChatSession(dbSession.Id, chat);
            foreach (var msg in dbMessages)
            {
                session.Messages.Add(new ChatMessage { IsUser = msg.IsUser, Content = msg.Content });
            }
            _sessions.Add(session);
        }

        NotifyStateChanged();
    }

    public async Task<ChatSession> CreateSessionAsync(string userId, string topic, CancellationToken cancellationToken = default)
    {
        var sessionId = await chatRepository.CreateSessionAsync(userId, topic, cancellationToken);
        var chat = await aiMessenger.CreateChatAsync(topic, cancellationToken);
        var session = new ChatSession(sessionId, chat);
        _sessions.Insert(0, session);
        CurrentSession = session;
        NotifyStateChanged();
        return session;
    }

    public void StartNewSession()
    {
        CurrentSession = null;
        NotifyStateChanged();
    }

    public void SelectSession(ChatSession session)
    {
        CurrentSession = session;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
