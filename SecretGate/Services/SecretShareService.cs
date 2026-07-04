using SecretGate.Models;

namespace SecretGate.Services;

public sealed class SecretShareService(
    ISecretRepository repository,
    SecretGateTokenGenerator tokenGenerator)
{
    public async Task<OneTimeShareReceipt> CreateShareAsync(
        string secret,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (expiresAtUtc <= DateTime.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiration must be in the future.");
        }

        var urlToken = tokenGenerator.CreateUrlToken();
        var accessKey = tokenGenerator.CreateAccessKey();
        var id = Guid.NewGuid();
        var envelope = SecretCrypto.Encrypt(
            secret,
            GetSharePassphrase(urlToken, accessKey),
            GetSharePurpose(urlToken));

        await repository.AddShareSecretAsync(
            id,
            HashUrlToken(urlToken),
            HashAccessKey(urlToken, accessKey),
            envelope,
            expiresAtUtc,
            DateTime.UtcNow,
            cancellationToken);

        return new OneTimeShareReceipt(urlToken, accessKey, expiresAtUtc);
    }

    public async Task<ConsumeShareResult> ConsumeShareAsync(
        string urlToken,
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);

        var normalizedAccessKey = SecretGateTokenGenerator.NormalizeAccessKey(accessKey);
        var record = await repository.ConsumeShareSecretAsync(
            HashUrlToken(urlToken),
            HashAccessKey(urlToken, normalizedAccessKey),
            DateTime.UtcNow,
            cancellationToken);

        if (record == null)
        {
            return new ConsumeShareResult(ConsumeShareStatus.NotFoundOrExpired, null);
        }

        var envelope = new SecretEncryptionEnvelope(record.Salt, record.Nonce, record.CipherText, record.Tag);
        if (!SecretCrypto.TryDecrypt(
            envelope,
            GetSharePassphrase(urlToken, normalizedAccessKey),
            GetSharePurpose(urlToken),
            out var secret))
        {
            return new ConsumeShareResult(ConsumeShareStatus.NotFoundOrExpired, null);
        }

        return new ConsumeShareResult(ConsumeShareStatus.Consumed, secret);
    }

    public static byte[] HashUrlToken(string urlToken)
    {
        return SecretCrypto.Sha256($"SecretGate.UrlToken.{urlToken}");
    }

    public static byte[] HashAccessKey(string urlToken, string accessKey)
    {
        return SecretCrypto.Sha256($"SecretGate.AccessKey.{urlToken}.{SecretGateTokenGenerator.NormalizeAccessKey(accessKey)}");
    }

    private static string GetSharePassphrase(string urlToken, string accessKey)
    {
        return $"{urlToken}:{SecretGateTokenGenerator.NormalizeAccessKey(accessKey)}";
    }

    private static string GetSharePurpose(string urlToken)
    {
        return $"SecretGate.Share.{urlToken}";
    }
}
