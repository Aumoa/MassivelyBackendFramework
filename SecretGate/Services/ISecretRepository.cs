using SecretGate.Models;

namespace SecretGate.Services;

public interface ISecretRepository
{
    Task<VaultProfileRecord?> GetVaultProfileAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default);

    Task InitializeVaultAsync(
        string ownerSubject,
        SecretEncryptionEnvelope profileEnvelope,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task ReplaceVaultEncryptionAsync(
        string ownerSubject,
        SecretEncryptionEnvelope profileEnvelope,
        IReadOnlyList<VaultSecretEncryptionUpdate> secretUpdates,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task ResetVaultAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredSecretRecord>> GetVaultSecretsAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default);

    Task AddVaultSecretAsync(
        string ownerSubject,
        Guid id,
        string name,
        SecretEncryptionEnvelope envelope,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task DeleteVaultSecretAsync(
        string ownerSubject,
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddShareSecretAsync(
        Guid id,
        byte[] urlTokenHash,
        byte[] accessKeyHash,
        SecretEncryptionEnvelope envelope,
        DateTime expiresAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<SharedSecretRecord?> ConsumeShareSecretAsync(
        byte[] urlTokenHash,
        byte[] accessKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
