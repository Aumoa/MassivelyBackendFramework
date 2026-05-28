using Microsoft.Extensions.Logging;

namespace DiscordBot.Services.Logging;

public sealed record ServerLogEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    string? Exception,
    IReadOnlyList<string> Scopes);

public sealed class ServerLogStore
{
    private const int MaxEntries = 2_000;

    private readonly Lock sync = new();
    private readonly LinkedList<ServerLogEntry> entries = [];
    private readonly List<Action> subscribers = [];
    private long sequence;

    public IReadOnlyList<ServerLogEntry> GetSnapshot()
    {
        lock (sync)
        {
            return entries.ToArray();
        }
    }

    public IDisposable Subscribe(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        lock (sync)
        {
            subscribers.Add(onChanged);
        }

        return new Subscription(this, onChanged);
    }

    public void Append(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception,
        IReadOnlyList<string> scopes)
    {
        Action[] callbacks;
        lock (sync)
        {
            entries.AddLast(new ServerLogEntry(
                ++sequence,
                DateTimeOffset.Now,
                level,
                category,
                eventId,
                message,
                exception?.ToString(),
                scopes));

            while (entries.Count > MaxEntries)
            {
                entries.RemoveFirst();
            }

            callbacks = subscribers.ToArray();
        }

        Notify(callbacks);
    }

    public void Clear()
    {
        Action[] callbacks;
        lock (sync)
        {
            entries.Clear();
            callbacks = subscribers.ToArray();
        }

        Notify(callbacks);
    }

    private void Unsubscribe(Action onChanged)
    {
        lock (sync)
        {
            subscribers.Remove(onChanged);
        }
    }

    private static void Notify(IReadOnlyList<Action> callbacks)
    {
        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch
            {
                // Logging must never fail the caller because a UI subscriber disconnected.
            }
        }
    }

    private sealed class Subscription(ServerLogStore store, Action onChanged) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            store.Unsubscribe(onChanged);
        }
    }
}
