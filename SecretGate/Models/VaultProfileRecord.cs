namespace SecretGate.Models;

public sealed record VaultProfileRecord(
    string OwnerSubject,
    byte[] Salt,
    byte[] Nonce,
    byte[] CipherText,
    byte[] Tag,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
