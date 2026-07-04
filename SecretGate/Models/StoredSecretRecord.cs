namespace SecretGate.Models;

public sealed record StoredSecretRecord(
    Guid Id,
    string Name,
    byte[] Salt,
    byte[] Nonce,
    byte[] CipherText,
    byte[] Tag,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
