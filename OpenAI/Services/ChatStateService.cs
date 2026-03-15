using OpenAI.Models;

namespace OpenAI.Services;

public class ChatStateService(IChatRepository chatRepository, IAIMessenger aiMessenger)
{
    private readonly List<ChatSession> m_Sessions = [];
    private bool m_Initialized;

    public IReadOnlyList<ChatSession> Sessions => m_Sessions;
    public ChatSession? CurrentSession { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (m_Initialized)
            return;

        m_Initialized = true;

        var dbSessions = await chatRepository.GetSessionsAsync(userId, cancellationToken);
        foreach (var dbSession in dbSessions)
        {
            var dbMessages = await chatRepository.GetMessagesAsync(dbSession.Id, cancellationToken);
            var chat = await aiMessenger.CreateChatAsync(dbSession.Topic, cancellationToken);
            var session = new ChatSession(dbSession.Id, chat);
            foreach (var msg in dbMessages)
            {
                session.Messages.Add(new ChatMessage { Role = msg.Role, Content = msg.Content, DbId = msg.Id });
            }
            m_Sessions.Add(session);
        }

        NotifyStateChanged();
    }

    public async Task<ChatSession> CreateSessionAsync(string userId, string topic, CancellationToken cancellationToken = default)
    {
        var sessionId = await chatRepository.CreateSessionAsync(userId, topic, cancellationToken);
        var chat = await aiMessenger.CreateChatAsync(topic, cancellationToken);
        var session = new ChatSession(sessionId, chat);
        m_Sessions.Insert(0, session);
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
