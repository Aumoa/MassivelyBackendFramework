using OpenAI.Models;

namespace OpenAI.Services;

public class ChatStateService
{
    private readonly List<ChatSession> _sessions = [];

    public IReadOnlyList<ChatSession> Sessions => _sessions;
    public ChatSession? CurrentSession { get; private set; }

    public event Action? OnChange;

    public void StartNewSession()
    {
        CurrentSession = null;
        NotifyStateChanged();
    }

    public void AddSession(ChatSession session)
    {
        _sessions.Insert(0, session);
        CurrentSession = session;
        NotifyStateChanged();
    }

    public void SelectSession(ChatSession session)
    {
        CurrentSession = session;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
