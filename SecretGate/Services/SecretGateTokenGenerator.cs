using System.Security.Cryptography;

namespace SecretGate.Services;

public sealed class SecretGateTokenGenerator
{
    private const string AccessKeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string CreateUrlToken()
    {
        return SecretCrypto.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    public string CreateAccessKey()
    {
        Span<char> chars = stackalloc char[9];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = i == 4
                ? '-'
                : AccessKeyAlphabet[RandomNumberGenerator.GetInt32(AccessKeyAlphabet.Length)];
        }

        return chars.ToString();
    }

    public static bool IsAccessKeyFormat(string accessKey)
    {
        if (accessKey.Length != 9 || accessKey[4] != '-')
        {
            return false;
        }

        for (var i = 0; i < accessKey.Length; i++)
        {
            if (i == 4)
            {
                continue;
            }

            if (!AccessKeyAlphabet.Contains(accessKey[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static string NormalizeAccessKey(string accessKey)
    {
        return accessKey.Trim().ToUpperInvariant();
    }
}
