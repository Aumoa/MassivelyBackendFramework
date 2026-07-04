using SecretGate.Models;
using System.Security.Cryptography;
using System.Text;

namespace SecretGate.Services;

public sealed class SecretVaultService(
    ISecretRepository repository,
    SecretVaultSession session)
{
    private const string VaultVerifier = "SecretGate.VaultProfile.Verifier.v1";

    public bool IsUnlocked => session.IsUnlocked;

    public async Task<bool> IsInitializedAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        return await repository.GetVaultProfileAsync(ownerSubject, cancellationToken) != null;
    }

    public async Task<bool> InitializeAsync(
        string ownerSubject,
        string vaultKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKey);

        var profileEnvelope = CreateProfileEnvelope(ownerSubject, vaultKey);
        var initialized = await repository.InitializeVaultAsync(
            ownerSubject,
            profileEnvelope,
            DateTime.UtcNow,
            cancellationToken);
        if (!initialized)
        {
            return false;
        }

        session.Unlock(vaultKey);
        return true;
    }

    public async Task<bool> TryUnlockAsync(
        string ownerSubject,
        string vaultKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKey);

        var profile = await repository.GetVaultProfileAsync(ownerSubject, cancellationToken);
        if (profile == null || !VerifyProfile(ownerSubject, profile, vaultKey))
        {
            session.Lock();
            return false;
        }

        session.Unlock(vaultKey);
        return true;
    }

    public void Lock()
    {
        session.Lock();
    }

    public async Task<bool> ChangePasswordAsync(
        string ownerSubject,
        string newVaultKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(newVaultKey);

        var currentVaultKey = session.GetVaultKeyOrThrow();
        var changed = await repository.ReplaceVaultEncryptionAsync(
            ownerSubject,
            (profile, records) =>
            {
                if (!VerifyProfile(ownerSubject, profile, currentVaultKey))
                {
                    session.Lock();
                    return null;
                }

                var updates = new List<VaultSecretEncryptionUpdate>(records.Count);
                foreach (var record in records)
                {
                    var envelope = new SecretEncryptionEnvelope(record.Salt, record.Nonce, record.CipherText, record.Tag);
                    if (!SecretCrypto.TryDecrypt(
                        envelope,
                        currentVaultKey,
                        GetVaultPurpose(ownerSubject, record.Id),
                        out var secret))
                    {
                        session.Lock();
                        return null;
                    }

                    updates.Add(new VaultSecretEncryptionUpdate(
                        record.Id,
                        SecretCrypto.Encrypt(
                            secret,
                            newVaultKey,
                            GetVaultPurpose(ownerSubject, record.Id))));
                }

                return new VaultEncryptionReplacement(
                    CreateProfileEnvelope(ownerSubject, newVaultKey),
                    updates);
            },
            DateTime.UtcNow,
            cancellationToken);
        if (!changed)
        {
            session.Lock();
            return false;
        }

        session.Unlock(newVaultKey);
        return true;
    }

    public async Task ResetAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        await repository.ResetVaultAsync(ownerSubject, cancellationToken);
        session.Lock();
    }

    public async Task<IReadOnlyList<StoredSecretView>> GetSecretsAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        var records = await repository.GetVaultSecretsAsync(ownerSubject, cancellationToken);
        var vaultKey = await TryGetVerifiedVaultKeyAsync(ownerSubject, cancellationToken);
        if (vaultKey == null)
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

    public async Task<bool> AddSecretAsync(
        string ownerSubject,
        string name,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        if (!session.IsUnlocked)
        {
            return false;
        }

        var vaultKey = session.GetVaultKeyOrThrow();
        var id = Guid.NewGuid();
        var envelope = SecretCrypto.Encrypt(
            secret,
            vaultKey,
            GetVaultPurpose(ownerSubject, id));

        var added = await repository.AddVaultSecretAsync(
            ownerSubject,
            profile => VerifyProfile(ownerSubject, profile, vaultKey),
            id,
            name.Trim(),
            envelope,
            DateTime.UtcNow,
            cancellationToken);
        if (!added)
        {
            session.Lock();
        }

        return added;
    }

    public async Task<bool> DeleteSecretAsync(
        string ownerSubject,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (await TryGetVerifiedVaultKeyAsync(ownerSubject, cancellationToken) == null)
        {
            return false;
        }

        var vaultKey = session.GetVaultKeyOrThrow();
        var deleted = await repository.DeleteVaultSecretAsync(
            ownerSubject,
            profile => VerifyProfile(ownerSubject, profile, vaultKey),
            id,
            cancellationToken);
        if (!deleted)
        {
            session.Lock();
        }

        return deleted;
    }

    private async Task<string?> TryGetVerifiedVaultKeyAsync(
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        if (!session.IsUnlocked)
        {
            return null;
        }

        var vaultKey = session.GetVaultKeyOrThrow();
        var profile = await repository.GetVaultProfileAsync(ownerSubject, cancellationToken);
        if (profile == null || !VerifyProfile(ownerSubject, profile, vaultKey))
        {
            session.Lock();
            return null;
        }

        return vaultKey;
    }

    private static string GetVaultPurpose(string ownerSubject, Guid secretId)
    {
        return $"SecretGate.Vault.{ownerSubject}.{secretId:N}";
    }

    private static string GetProfilePurpose(string ownerSubject)
    {
        return $"SecretGate.VaultProfile.{ownerSubject}";
    }

    private static SecretEncryptionEnvelope CreateProfileEnvelope(string ownerSubject, string vaultKey)
    {
        return SecretCrypto.Encrypt(VaultVerifier, vaultKey, GetProfilePurpose(ownerSubject));
    }

    private static bool VerifyProfile(string ownerSubject, VaultProfileRecord profile, string vaultKey)
    {
        var envelope = new SecretEncryptionEnvelope(profile.Salt, profile.Nonce, profile.CipherText, profile.Tag);
        if (!SecretCrypto.TryDecrypt(envelope, vaultKey, GetProfilePurpose(ownerSubject), out var verifier))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(VaultVerifier);
        var actual = Encoding.UTF8.GetBytes(verifier);
        return actual.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
