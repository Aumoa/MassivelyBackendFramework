using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class SecretCryptoTests
{
    [Fact]
    public void EncryptRoundTripsWithMatchingPassphrase()
    {
        var envelope = SecretCrypto.Encrypt("alpha secret", "vault-key", "test-purpose");

        var decrypted = SecretCrypto.TryDecrypt(envelope, "vault-key", "test-purpose", out var secret);

        Assert.True(decrypted);
        Assert.Equal("alpha secret", secret);
        Assert.NotEqual("alpha secret"u8.ToArray(), envelope.CipherText);
    }

    [Fact]
    public void DecryptRejectsWrongPassphrase()
    {
        var envelope = SecretCrypto.Encrypt("alpha secret", "vault-key", "test-purpose");

        var decrypted = SecretCrypto.TryDecrypt(envelope, "wrong-key", "test-purpose", out var secret);

        Assert.False(decrypted);
        Assert.Equal(string.Empty, secret);
    }

    [Fact]
    public void DecryptRejectsWrongPurpose()
    {
        var envelope = SecretCrypto.Encrypt("alpha secret", "vault-key", "test-purpose");

        var decrypted = SecretCrypto.TryDecrypt(envelope, "vault-key", "other-purpose", out _);

        Assert.False(decrypted);
    }

    [Fact]
    public void CanDecryptValidatesWithoutReturningPlainText()
    {
        var envelope = SecretCrypto.Encrypt("alpha secret", "vault-key", "test-purpose");

        Assert.True(SecretCrypto.CanDecrypt(envelope, "vault-key", "test-purpose"));
        Assert.False(SecretCrypto.CanDecrypt(envelope, "wrong-key", "test-purpose"));
    }
}
