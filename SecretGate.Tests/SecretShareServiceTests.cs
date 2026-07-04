using SecretGate.Models;
using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class SecretShareServiceTests
{
    [Fact]
    public async Task SharedSecretConsumesOnlyOnce()
    {
        var repository = new FakeSecretRepository();
        var service = new SecretShareService(repository, new SecretGateTokenGenerator());

        var receipt = await service.CreateShareAsync("single view secret", DateTime.UtcNow.AddMinutes(5));

        Assert.NotNull(repository.LastShareEnvelope);
        Assert.NotEqual("single view secret"u8.ToArray(), repository.LastShareEnvelope.CipherText);

        var wrongKey = await service.ConsumeShareAsync(receipt.UrlToken, "AAAA-AAAA");
        Assert.Equal(ConsumeShareStatus.NotFoundOrExpired, wrongKey.Status);

        var consumed = await service.ConsumeShareAsync(receipt.UrlToken, receipt.AccessKey);
        Assert.Equal(ConsumeShareStatus.Consumed, consumed.Status);
        Assert.Equal("single view secret", consumed.Secret);

        var secondRead = await service.ConsumeShareAsync(receipt.UrlToken, receipt.AccessKey);
        Assert.Equal(ConsumeShareStatus.NotFoundOrExpired, secondRead.Status);
    }

    private sealed class FakeSecretRepository : ISecretRepository
    {
        private StoredShare? m_Share;

        public SecretEncryptionEnvelope? LastShareEnvelope { get; private set; }

        public Task<IReadOnlyList<StoredSecretRecord>> GetVaultSecretsAsync(
            string ownerSubject,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StoredSecretRecord>>([]);
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
            LastShareEnvelope = envelope;
            m_Share = new StoredShare(
                urlTokenHash,
                accessKeyHash,
                new SharedSecretRecord(
                    id,
                    envelope.Salt,
                    envelope.Nonce,
                    envelope.CipherText,
                    envelope.Tag,
                    expiresAtUtc,
                    nowUtc));

            return Task.CompletedTask;
        }

        public Task<SharedSecretRecord?> ConsumeShareSecretAsync(
            byte[] urlTokenHash,
            byte[] accessKeyHash,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            if (m_Share == null ||
                m_Share.Record.ExpiresAtUtc <= nowUtc ||
                !m_Share.UrlTokenHash.SequenceEqual(urlTokenHash) ||
                !m_Share.AccessKeyHash.SequenceEqual(accessKeyHash))
            {
                return Task.FromResult<SharedSecretRecord?>(null);
            }

            var record = m_Share.Record;
            m_Share = null;
            return Task.FromResult<SharedSecretRecord?>(record);
        }

        private sealed record StoredShare(
            byte[] UrlTokenHash,
            byte[] AccessKeyHash,
            SharedSecretRecord Record);
    }
}
