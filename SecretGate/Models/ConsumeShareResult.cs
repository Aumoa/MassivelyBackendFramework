namespace SecretGate.Models;

public enum ConsumeShareStatus
{
    Consumed,
    NotFoundOrExpired
}

public sealed record ConsumeShareResult(
    ConsumeShareStatus Status,
    string? Secret);
