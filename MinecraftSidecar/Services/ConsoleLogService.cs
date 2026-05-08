namespace MinecraftSidecar.Services;

public enum ConsoleLogLevel
{
    Info,
    Command,
    Response,
    Error,
}

public sealed record ConsoleLogEntry(DateTime Timestamp, ConsoleLogLevel Level, string Message);

/// <summary>
/// Singleton service that holds a shared, bounded console log buffer for all connected users.
/// The buffer is cleared 30 minutes after the last subscriber disconnects.
/// </summary>
public sealed class ConsoleLogService : IDisposable
{
    private const int MaxEntries = 500;
    private static readonly TimeSpan ExpiryDelay = TimeSpan.FromMinutes(30);

    private readonly object _lock = new();
    private readonly LinkedList<ConsoleLogEntry> _entries = new();
    private readonly List<Func<Task>> _subscribers = new();

    private System.Threading.Timer? _expiryTimer;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Public read access
    // -------------------------------------------------------------------------

    /// <summary>Returns a snapshot of all current log entries.</summary>
    public IReadOnlyList<ConsoleLogEntry> GetSnapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    // -------------------------------------------------------------------------
    // Subscription management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a callback that will be invoked (on the caller's thread context)
    /// whenever new entries are appended.  Returns an IDisposable that unsubscribes.
    /// </summary>
    public IDisposable Subscribe(Func<Task> onNewEntry)
    {
        lock (_lock)
        {
            _subscribers.Add(onNewEntry);
            CancelExpiry();
        }

        return new Subscription(this, onNewEntry);
    }

    private void Unsubscribe(Func<Task> onNewEntry)
    {
        lock (_lock)
        {
            _subscribers.Remove(onNewEntry);

            if (_subscribers.Count == 0)
            {
                ScheduleExpiry();
            }
        }
    }

    // -------------------------------------------------------------------------
    // Writing
    // -------------------------------------------------------------------------

    public void Append(ConsoleLogLevel level, string message)
    {
        Func<Task>[] callbacks;

        lock (_lock)
        {
            _entries.AddLast(new ConsoleLogEntry(DateTime.Now, level, message));

            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveFirst();
            }

            callbacks = _subscribers.ToArray();
        }

        // Notify outside lock to avoid deadlocks.
        foreach (var cb in callbacks)
        {
            _ = cb();
        }
    }

    // -------------------------------------------------------------------------
    // Expiry logic
    // -------------------------------------------------------------------------

    private void ScheduleExpiry()
    {
        // Called inside _lock – safe to replace the timer.
        _expiryTimer?.Dispose();
        _expiryTimer = new System.Threading.Timer(_ => ClearBuffer(), null, ExpiryDelay, Timeout.InfiniteTimeSpan);
    }

    private void CancelExpiry()
    {
        // Called inside _lock.
        _expiryTimer?.Dispose();
        _expiryTimer = null;
    }

    private void ClearBuffer()
    {
        lock (_lock)
        {
            // Double-check: a subscriber may have arrived while the timer was firing.
            if (_subscribers.Count == 0)
            {
                _entries.Clear();
            }
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _expiryTimer?.Dispose();
            _expiryTimer = null;
        }
    }

    // -------------------------------------------------------------------------
    // Inner subscription handle
    // -------------------------------------------------------------------------

    private sealed class Subscription : IDisposable
    {
        private readonly ConsoleLogService _service;
        private readonly Func<Task> _callback;
        private bool _disposed;

        public Subscription(ConsoleLogService service, Func<Task> callback)
        {
            _service = service;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.Unsubscribe(_callback);
        }
    }
}
