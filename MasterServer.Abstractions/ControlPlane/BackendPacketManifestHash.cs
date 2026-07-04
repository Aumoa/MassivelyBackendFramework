using System;

namespace MasterServer.ControlPlane;

public readonly struct BackendPacketManifestHash : IEquatable<BackendPacketManifestHash>
{
    public const int HexLength = 64;

    public BackendPacketManifestHash(string value)
    {
        Value = Normalize(value);
    }

    public string Value { get; }

    public bool Equals(BackendPacketManifestHash other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is BackendPacketManifestHash other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    public static BackendPacketManifestHash FromBytes(byte[] hashBytes)
    {
        if (hashBytes == null)
        {
            throw new ArgumentNullException(nameof(hashBytes));
        }

        var chars = new char[hashBytes.Length * 2];
        for (var i = 0; i < hashBytes.Length; i++)
        {
            var b = hashBytes[i];
            chars[i * 2] = ToHexChar(b >> 4);
            chars[i * 2 + 1] = ToHexChar(b & 0xF);
        }

        return new BackendPacketManifestHash(new string(chars));
    }

    public static bool operator ==(BackendPacketManifestHash left, BackendPacketManifestHash right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BackendPacketManifestHash left, BackendPacketManifestHash right)
    {
        return !left.Equals(right);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Backend packet manifest hash is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length != HexLength)
        {
            throw new ArgumentException($"Backend packet manifest hash must be {HexLength} hexadecimal characters.", nameof(value));
        }

        foreach (var c in normalized)
        {
            if (!IsHex(c))
            {
                throw new ArgumentException("Backend packet manifest hash must be hexadecimal.", nameof(value));
            }
        }

        return normalized.ToLowerInvariant();
    }

    private static bool IsHex(char c)
    {
        return c is >= '0' and <= '9' ||
               c is >= 'a' and <= 'f' ||
               c is >= 'A' and <= 'F';
    }

    private static char ToHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + value - 10);
    }
}
