using SecretGate.Models;

namespace SecretGate.Services;

public sealed class SecretVaultService(
    ISecretRepository repository,
    SecretVaultSession session)
{
    public bool IsUnlocked => session.IsUnlocked;

    public void Unlock(string vaultKey)
    {
        session.Unlock(vaultKey);
    }

    public async Task<bool> TryUnlockAsync(
        string ownerSubject,
        string vaultKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKey);

        var records = await repository.GetVaultSecretsAsync(ownerSubject, cancellationToken);
        foreach (var record in records)
        {
            var envelope = new SecretEncryptionEnvelope(record.Salt, record.Nonce, record.CipherText, record.Tag);
            if (!SecretCrypto.CanDecrypt(envelope, vaultKey, GetVaultPurpose(ownerSubject, record.Id)))
            {
                session.Lock();
                return false;
            }
        }

        session.Unlock(vaultKey);
        return true;
    }

    public void Lock()
    {
        session.Lock();
    }

    public async Task<IReadOnlyList<StoredSecretView>> GetSecretsAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        var records = await repository.GetVaultSecretsAsync(ownerSubject, cancellationToken);
        if (!session.IsUnlocked)
        {
            return records
                .Select(static record => new StoredSecretView(
                    record.Id,
                    record.Name,
                    null,
                    false,
                    record.CreatedAtUtc,
                    record.UpdatedAtUtc))
                .ToArray();
        }

        var vaultKey = session.GetVaultKeyOrThrow();
        return records
            .Select(record =>
            {
                var envelope = new SecretEncryptionEnvelope(record.Salt, record.Nonce, record.CipherText, record.Tag);
                var decrypted = SecretCrypto.TryDecrypt(
                    envelope,
                    vaultKey,
                    GetVaultPurpose(ownerSubject, record.Id),
                    out var secret);

                return new StoredSecretView(
                    record.Id,
                    record.Name,
                    decrypted ? secret : null,
                    decrypted,
                    record.CreatedAtUtc,
                    record.UpdatedAtUtc);
            })
            .ToArray();
    }

    public async Task AddSecretAsync(
        string ownerSubject,
        string name,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var id = Guid.NewGuid();
        var envelope = SecretCrypto.Encrypt(
            secret,
            session.GetVaultKeyOrThrow(),
            GetVaultPurpose(ownerSubject, id));

        await repository.AddVaultSecretAsync(
            ownerSubject,
            id,
            name.Trim(),
            envelope,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task DeleteSecretAsync(
        string ownerSubject,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return repository.DeleteVaultSecretAsync(ownerSubject, id, cancellationToken);
    }

    private static string GetVaultPurpose(string ownerSubject, Guid secretId)
    {
        return $"SecretGate.Vault.{ownerSubject}.{secretId:N}";
    }
}
