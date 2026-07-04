using SecretGate.Models;
using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class SecretVaultServiceTests
{
    [Fact]
    public async Task TryUnlockRejectsWrongKeyWhenVaultHasSecrets()
    {
        const string ownerSubject = "user-sub";
        var secretId = Guid.NewGuid();
        var envelope = SecretCrypto.Encrypt(
            "vault secret",
            "correct-key",
            GetVaultPurpose(ownerSubject, secretId));
        var repository = new FakeSecretRepository(
            new StoredSecretRecord(
                secretId,
                "database password",
                envelope.Salt,
                envelope.Nonce,
                envelope.CipherText,
                envelope.Tag,
                DateTime.UtcNow,
                DateTime.UtcNow));
        var service = new SecretVaultService(repository, new SecretVaultSession());

        var unlocked = await service.TryUnlockAsync(ownerSubject, "wrong-key");

        Assert.False(unlocked);
        Assert.False(service.IsUnlocked);
    }

    [Fact]
    public async Task TryUnlockAllowsCorrectKeyAndDisplaysSecret()
    {
        const string ownerSubject = "user-sub";
        var secretId = Guid.NewGuid();
        var envelope = SecretCrypto.Encrypt(
            "vault secret",
            "correct-key",
            GetVaultPurpose(ownerSubject, secretId));
        var repository = new FakeSecretRepository(
            new StoredSecretRecord(
                secretId,
                "database password",
                envelope.Salt,
                envelope.Nonce,
                envelope.CipherText,
                envelope.Tag,
                DateTime.UtcNow,
                DateTime.UtcNow));
        var service = new SecretVaultService(repository, new SecretVaultSession());

        var unlocked = await service.TryUnlockAsync(ownerSubject, "correct-key");
        var secrets = await service.GetSecretsAsync(ownerSubject);

        Assert.True(unlocked);
        Assert.True(service.IsUnlocked);
        var secret = Assert.Single(secrets);
        Assert.True(secret.IsDecrypted);
        Assert.Equal("vault secret", secret.Secret);
    }

    private static string GetVaultPurpose(string ownerSubject, Guid secretId)
    {
        return $"SecretGate.Vault.{ownerSubject}.{secretId:N}";
    }

    private sealed class FakeSecretRepository(params StoredSecretRecord[] records) : ISecretRepository
    {
        public Task<IReadOnlyList<StoredSecretRecord>> GetVaultSecretsAsync(
            string ownerSubject,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StoredSecretRecord>>(records);
        }

        public Task AddVaultSecretAsync(
            string ownerSubject,
            Guid id,
            string name,
            SecretEncryptionEnvelope envelope,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteVaultSecretAsync(
            string ownerSubject,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AddShareSecretAsync(
            Guid id,
            byte[] urlTokenHash,
            byte[] accessKeyHash,
            SecretEncryptionEnvelope envelope,
            DateTime expiresAtUtc,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<SharedSecretRecord?> ConsumeShareSecretAsync(
            byte[] urlTokenHash,
            byte[] accessKeyHash,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SharedSecretRecord?>(null);
        }
    }
}
