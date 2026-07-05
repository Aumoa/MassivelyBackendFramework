namespace SecretGate.Services;

public sealed record SecretEncryptionEnvelope(
    byte[] Salt,
    byte[] Nonce,
    byte[] CipherText,
    byte[] Tag);
