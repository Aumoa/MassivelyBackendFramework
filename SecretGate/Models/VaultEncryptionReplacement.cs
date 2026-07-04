using SecretGate.Services;

namespace SecretGate.Models;

public sealed record VaultEncryptionReplacement(
    SecretEncryptionEnvelope ProfileEnvelope,
    IReadOnlyList<VaultSecretEncryptionUpdate> SecretUpdates);
