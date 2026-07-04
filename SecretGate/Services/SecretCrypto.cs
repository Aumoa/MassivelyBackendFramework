using System.Security.Cryptography;
using System.Text;

namespace SecretGate.Services;

public static class SecretCrypto
{
    public const int SaltLength = 16;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int KeyLength = 32;
    public const int KeyDerivationIterations = 210_000;

    public static SecretEncryptionEnvelope Encrypt(string secret, string passphrase, string purpose)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plainText = Encoding.UTF8.GetBytes(secret);
        var cipherText = new byte[plainText.Length];
        var tag = new byte[TagLength];

        using var key = DeriveKey(passphrase, purpose, salt);
        using var aes = new AesGcm(key.KeyBytes, TagLength);
        aes.Encrypt(nonce, plainText, cipherText, tag);
        CryptographicOperations.ZeroMemory(plainText);

        return new SecretEncryptionEnvelope(salt, nonce, cipherText, tag);
    }

    public static bool TryDecrypt(
        SecretEncryptionEnvelope envelope,
        string passphrase,
        string purpose,
        out string secret)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        secret = string.Empty;
        var plainText = new byte[envelope.CipherText.Length];
        try
        {
            using var key = DeriveKey(passphrase, purpose, envelope.Salt);
            using var aes = new AesGcm(key.KeyBytes, TagLength);
            aes.Decrypt(envelope.Nonce, envelope.CipherText, envelope.Tag, plainText);
            secret = Encoding.UTF8.GetString(plainText);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainText);
        }
    }

    public static byte[] Sha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    public static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static DerivedKey DeriveKey(string passphrase, string purpose, byte[] salt)
    {
        var scopedPassphrase = $"{purpose}\0{passphrase}";
        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            scopedPassphrase,
            salt,
            KeyDerivationIterations,
            HashAlgorithmName.SHA256,
            KeyLength);

        return new DerivedKey(keyBytes);
    }

    private sealed class DerivedKey(byte[] keyBytes) : IDisposable
    {
        public byte[] KeyBytes { get; } = keyBytes;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(KeyBytes);
        }
    }
}
