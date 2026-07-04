namespace SecretGate.Models;

public sealed record SharedSecretRecord(
    Guid Id,
    byte[] Salt,
    byte[] Nonce,
    byte[] CipherText,
    byte[] Tag,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc);
