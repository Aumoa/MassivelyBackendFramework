using OAuth2.Services;

namespace OAuth2.Controllers.Tests;

public sealed class LoginAttemptLimiterTests
{
    [Fact]
    public void RecordFailure_LocksIdentifierAfterThreshold()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new LoginAttemptLimiter(timeProvider);

        for (var i = 0; i < LoginAttemptLimiter.MaxIdentifierFailures; i++)
        {
            Assert.True(limiter.IsAllowed("alice", "203.0.113.10", out _));
            limiter.RecordFailure("alice", "203.0.113.10");
        }

        Assert.False(limiter.IsAllowed("alice", "203.0.113.10", out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);

        timeProvider.Advance(LoginAttemptLimiter.Lockout);

        Assert.True(limiter.IsAllowed("alice", "203.0.113.10", out _));
    }

    [Fact]
    public void RecordFailure_NormalizesIdentifierCase()
    {
        var limiter = new LoginAttemptLimiter(new ManualTimeProvider());

        for (var i = 0; i < LoginAttemptLimiter.MaxIdentifierFailures; i++)
        {
            limiter.RecordFailure("alice", null);
        }

        Assert.False(limiter.IsAllowed("ALICE", null, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void RecordFailure_LocksOriginAfterBroadEnumeration()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new LoginAttemptLimiter(timeProvider);
        const string origin = "203.0.113.11";

        for (var i = 0; i < LoginAttemptLimiter.MaxOriginFailures; i++)
        {
            var identifier = "user-" + i;
            Assert.True(limiter.IsAllowed(identifier, origin, out _));
            limiter.RecordFailure(identifier, origin);
        }

        Assert.False(limiter.IsAllowed("another-user", origin, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void RecordSuccess_ClearsPriorFailures()
    {
        var limiter = new LoginAttemptLimiter(new ManualTimeProvider());

        for (var i = 0; i < LoginAttemptLimiter.MaxIdentifierFailures - 1; i++)
        {
            limiter.RecordFailure("alice", "203.0.113.12");
        }

        limiter.RecordSuccess("alice", "203.0.113.12");

        for (var i = 0; i < LoginAttemptLimiter.MaxIdentifierFailures - 1; i++)
        {
            Assert.True(limiter.IsAllowed("alice", "203.0.113.12", out _));
            limiter.RecordFailure("alice", "203.0.113.12");
        }

        Assert.True(limiter.IsAllowed("alice", "203.0.113.12", out _));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset m_UtcNow = new(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return m_UtcNow;
        }

        public void Advance(TimeSpan value)
        {
            m_UtcNow += value;
        }
    }
}
