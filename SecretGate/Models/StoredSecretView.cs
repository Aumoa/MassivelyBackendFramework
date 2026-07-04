namespace SecretGate.Models;

public sealed record StoredSecretView(
    Guid Id,
    string Name,
    string? Secret,
    bool IsDecrypted,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
