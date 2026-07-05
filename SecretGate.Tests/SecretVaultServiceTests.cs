using SecretGate.Models;
using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class SecretVaultServiceTests
{
    [Fact]
    public async Task TryUnlockRejectsAnyPasswordBeforeVaultIsInitialized()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretVaultService(repository, new SecretVaultSession());

        var unlocked = await service.TryUnlockAsync("user-sub", "anything");

        Assert.False(unlocked);
        Assert.False(service.IsUnlocked);
    }

    [Fact]
    public async Task InitializeCreatesPasswordProtectedVault()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretVaultService(repository, new SecretVaultSession());

        var initialized = await service.InitializeAsync("user-sub", "correct-password");
        service.Lock();

        var wrongPassword = await service.TryUnlockAsync("user-sub", "wrong-password");
        var correctPassword = await service.TryUnlockAsync("user-sub", "correct-password");

        Assert.True(initialized);
        Assert.True(await service.IsInitializedAsync("user-sub"));
        Assert.False(wrongPassword);
        Assert.True(correctPassword);
        Assert.True(service.IsUnlocked);
    }

    [Fact]
    public async Task InitializeDoesNotOverwriteExistingVault()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretVaultService(repository, new SecretVaultSession());
        var staleSetupService = new SecretVaultService(repository, new SecretVaultSession());

        Assert.True(await service.InitializeAsync("user-sub", "correct-password"));
        Assert.True(await service.AddSecretAsync("user-sub", "database password", "vault secret"));

        var duplicateInitialized = await staleSetupService.InitializeAsync("user-sub", "new-password");
        service.Lock();

        Assert.False(duplicateInitialized);
        Assert.False(staleSetupService.IsUnlocked);
        Assert.False(await service.TryUnlockAsync("user-sub", "new-password"));
        Assert.True(await service.TryUnlockAsync("user-sub", "correct-password"));
        var secret = Assert.Single(await service.GetSecretsAsync("user-sub"));
        Assert.True(secret.IsDecrypted);
        Assert.Equal("vault secret", secret.Secret);
    }

    [Fact]
    public async Task ChangePasswordReencryptsSavedSecrets()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretVaultService(repository, new SecretVaultSession());
        await service.InitializeAsync("user-sub", "old-password");
        await service.AddSecretAsync("user-sub", "database password", "vault secret");

        var changed = await service.ChangePasswordAsync("user-sub", "new-password");
        service.Lock();
        var oldPassword = await service.TryUnlockAsync("user-sub", "old-password");
        var newPassword = await service.TryUnlockAsync("user-sub", "new-password");
        var secrets = await service.GetSecretsAsync("user-sub");

        Assert.True(changed);
        Assert.False(oldPassword);
        Assert.True(newPassword);
        var secret = Assert.Single(secrets);
        Assert.True(secret.IsDecrypted);
        Assert.Equal("vault secret", secret.Secret);
    }

    [Fact]
    public async Task AddSecretRejectsStaleUnlockedVaultKey()
    {
        var repository = new FakeSecretRepository();
        var staleSessionService = new SecretVaultService(repository, new SecretVaultSession());
        var currentSessionService = new SecretVaultService(repository, new SecretVaultSession());

        await staleSessionService.InitializeAsync("user-sub", "old-password");
        Assert.True(await currentSessionService.TryUnlockAsync("user-sub", "old-password"));
        Assert.True(await currentSessionService.ChangePasswordAsync("user-sub", "new-password"));

        var savedWithStaleKey = await staleSessionService.AddSecretAsync(
            "user-sub",
            "database password",
            "vault secret");

        Assert.False(savedWithStaleKey);
        Assert.False(staleSessionService.IsUnlocked);
        Assert.Empty(await currentSessionService.GetSecretsAsync("user-sub"));
    }

    [Fact]
    public async Task ResetDeletesVaultProfileAndSavedSecrets()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretVaultService(repository, new SecretVaultSession());
        await service.InitializeAsync("user-sub", "password");
        await service.AddSecretAsync("user-sub", "database password", "vault secret");

        await service.ResetAsync("user-sub");

        Assert.False(await service.IsInitializedAsync("user-sub"));
        Assert.False(service.IsUnlocked);
        Assert.Empty(await service.GetSecretsAsync("user-sub"));
    }

    private sealed class FakeSecretRepository : ISecretRepository
    {
        private readonly List<StoredSecretRecord> m_Records = [];
        private VaultProfileRecord? m_Profile;

        public Task<VaultProfileRecord?> GetVaultProfileAsync(
            string ownerSubject,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(m_Profile);
        }

        public Task<bool> InitializeVaultAsync(
            string ownerSubject,
            SecretEncryptionEnvelope profileEnvelope,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            if (m_Profile != null)
            {
                return Task.FromResult(false);
            }

            m_Profile = CreateProfile(ownerSubject, profileEnvelope, nowUtc);
            return Task.FromResult(true);
        }

        public Task<bool> ReplaceVaultEncryptionAsync(
            string ownerSubject,
            Func<VaultProfileRecord, IReadOnlyList<StoredSecretRecord>, VaultEncryptionReplacement?> replacementFactory,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            if (m_Profile == null)
            {
                return Task.FromResult(false);
            }

            var replacement = replacementFactory(m_Profile, [.. m_Records]);
            if (replacement == null)
            {
                return Task.FromResult(false);
            }

            m_Profile = CreateProfile(ownerSubject, replacement.ProfileEnvelope, nowUtc);
            foreach (var update in replacement.SecretUpdates)
            {
                var index = m_Records.FindIndex(record => record.Id == update.Id);
                if (index < 0)
                {
                    continue;
                }

                var existing = m_Records[index];
                m_Records[index] = existing with
                {
                    Salt = update.Envelope.Salt,
                    Nonce = update.Envelope.Nonce,
                    CipherText = update.Envelope.CipherText,
                    Tag = update.Envelope.Tag,
                    UpdatedAtUtc = nowUtc
                };
            }

            return Task.FromResult(true);
        }

        public Task ResetVaultAsync(
            string ownerSubject,
            CancellationToken cancellationToken = default)
        {
            m_Profile = null;
            m_Records.Clear();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredSecretRecord>> GetVaultSecretsAsync(
            string ownerSubject,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StoredSecretRecord>>([.. m_Records]);
        }

        public Task<bool> AddVaultSecretAsync(
            string ownerSubject,
            Func<VaultProfileRecord, bool> profileValidator,
            Guid id,
            string name,
            SecretEncryptionEnvelope envelope,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            if (m_Profile == null || !profileValidator(m_Profile))
            {
                return Task.FromResult(false);
            }

            m_Records.Add(new StoredSecretRecord(
                id,
                name,
                envelope.Salt,
                envelope.Nonce,
                envelope.CipherText,
                envelope.Tag,
                nowUtc,
                nowUtc));

            return Task.FromResult(true);
        }

        public Task<bool> DeleteVaultSecretAsync(
            string ownerSubject,
            Func<VaultProfileRecord, bool> profileValidator,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            if (m_Profile == null || !profileValidator(m_Profile))
            {
                return Task.FromResult(false);
            }

            m_Records.RemoveAll(record => record.Id == id);
            return Task.FromResult(true);
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

        private static VaultProfileRecord CreateProfile(
            string ownerSubject,
            SecretEncryptionEnvelope envelope,
            DateTime nowUtc)
        {
            return new VaultProfileRecord(
                ownerSubject,
                envelope.Salt,
                envelope.Nonce,
                envelope.CipherText,
                envelope.Tag,
                nowUtc,
                nowUtc);
        }
    }
}
