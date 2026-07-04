namespace OAuth2.Services;

public interface ILoginAttemptLimiter
{
    bool IsAllowed(string identifier, string? origin, out TimeSpan retryAfter);

    void RecordFailure(string identifier, string? origin);

    void RecordSuccess(string identifier, string? origin);
}
