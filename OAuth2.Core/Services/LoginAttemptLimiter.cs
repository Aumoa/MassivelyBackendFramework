using System.Collections.Concurrent;

namespace OAuth2.Services;

public sealed class LoginAttemptLimiter(TimeProvider timeProvider) : ILoginAttemptLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    public const int MaxIdentifierFailures = 5;
    public const int MaxOriginFailures = 30;

    private readonly ConcurrentDictionary<string, AttemptState> m_Attempts = new(StringComparer.Ordinal);

    public bool IsAllowed(string identifier, string? origin, out TimeSpan retryAfter)
    {
        var now = timeProvider.GetUtcNow();
        CleanupExpired(now);

        retryAfter = GetRetryAfter(CreateKey("id", NormalizeIdentifier(identifier)), now);
        if (retryAfter > TimeSpan.Zero)
        {
            return false;
        }

        var normalizedOrigin = NormalizeOrigin(origin);
        if (normalizedOrigin == null)
        {
            return true;
        }

        retryAfter = GetRetryAfter(CreateKey("origin", normalizedOrigin), now);
        return retryAfter <= TimeSpan.Zero;
    }

    public void RecordFailure(string identifier, string? origin)
    {
        var now = timeProvider.GetUtcNow();
        AddFailure(CreateKey("id", NormalizeIdentifier(identifier)), MaxIdentifierFailures, now);

        var normalizedOrigin = NormalizeOrigin(origin);
        if (normalizedOrigin != null)
        {
            AddFailure(CreateKey("origin", normalizedOrigin), MaxOriginFailures, now);
        }
    }

    public void RecordSuccess(string identifier, string? origin)
    {
        m_Attempts.TryRemove(CreateKey("id", NormalizeIdentifier(identifier)), out _);
    }

    private void AddFailure(string key, int maxFailures, DateTimeOffset now)
    {
        m_Attempts.AddOrUpdate(
            key,
            _ => new AttemptState(now, 1, null),
            (_, current) =>
            {
                var state = current.ResetIfExpired(now);
                var failures = state.Failures + 1;
                var lockedUntil = failures >= maxFailures
                    ? now.Add(Lockout)
                    : state.LockedUntil;

                return state with
                {
                    Failures = failures,
                    LockedUntil = lockedUntil
                };
            });
    }

    private TimeSpan GetRetryAfter(string key, DateTimeOffset now)
    {
        return m_Attempts.TryGetValue(key, out var state) &&
               state.LockedUntil is { } lockedUntil &&
               lockedUntil > now
            ? lockedUntil - now
            : TimeSpan.Zero;
    }

    private void CleanupExpired(DateTimeOffset now)
    {
        foreach (var (key, state) in m_Attempts)
        {
            if (state.LockedUntil is { } lockedUntil && lockedUntil > now)
            {
                continue;
            }

            if (now - state.WindowStartedAt <= Window)
            {
                continue;
            }

            m_Attempts.TryRemove(key, out _);
        }
    }

    private static string NormalizeIdentifier(string identifier)
    {
        return identifier.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOrigin(string? origin)
    {
        return string.IsNullOrWhiteSpace(origin) ? null : origin.Trim();
    }

    private static string CreateKey(string kind, string value)
    {
        return kind + ":" + value;
    }

    private sealed record AttemptState(DateTimeOffset WindowStartedAt, int Failures, DateTimeOffset? LockedUntil)
    {
        public AttemptState ResetIfExpired(DateTimeOffset now)
        {
            return now - WindowStartedAt > Window
                ? new AttemptState(now, 0, null)
                : this;
        }
    }
}
