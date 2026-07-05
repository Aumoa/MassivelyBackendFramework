using SecretGate.Services;

namespace SecretGate.Models;

public sealed record VaultSecretEncryptionUpdate(
    Guid Id,
    SecretEncryptionEnvelope Envelope);
