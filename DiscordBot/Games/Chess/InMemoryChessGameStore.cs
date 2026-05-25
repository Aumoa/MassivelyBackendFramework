namespace DiscordBot.Games.Chess;

internal sealed class InMemoryChessGameStore : IChessGameStore
{
    private readonly object m_Lock = new();
    private readonly Dictionary<string, ChessGameSession> m_SessionsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> m_ActiveSessionIdsByUserId = new(StringComparer.OrdinalIgnoreCase);

    public ChessGameSession? FindActiveByUser(string userId)
    {
        lock (m_Lock)
        {
            if (!m_ActiveSessionIdsByUserId.TryGetValue(userId, out var sessionId)
                || !m_SessionsById.TryGetValue(sessionId, out var session)
                || !session.IsActive)
            {
                return null;
            }

            return session;
        }
    }

    public ValueTask AddAsync(ChessGameSession session, CancellationToken cancellationToken = default)
    {
        lock (m_Lock)
        {
            m_SessionsById[session.Id] = session;
            IndexParticipant(session.White, session.Id);
            IndexParticipant(session.Black, session.Id);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(ChessGameSession session, CancellationToken cancellationToken = default)
    {
        lock (m_Lock)
        {
            m_SessionsById.Remove(session.Id);
            RemoveParticipant(session.White, session.Id);
            RemoveParticipant(session.Black, session.Id);
        }

        return ValueTask.CompletedTask;
    }

    private void IndexParticipant(ChessParticipant participant, string sessionId)
    {
        if (!participant.IsAi && !string.IsNullOrWhiteSpace(participant.UserId))
        {
            m_ActiveSessionIdsByUserId[participant.UserId] = sessionId;
        }
    }

    private void RemoveParticipant(ChessParticipant participant, string sessionId)
    {
        if (!participant.IsAi
            && !string.IsNullOrWhiteSpace(participant.UserId)
            && m_ActiveSessionIdsByUserId.TryGetValue(participant.UserId, out var indexedSessionId)
            && indexedSessionId == sessionId)
        {
            m_ActiveSessionIdsByUserId.Remove(participant.UserId);
        }
    }
}
