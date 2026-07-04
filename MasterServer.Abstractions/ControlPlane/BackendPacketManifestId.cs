using System;

namespace MasterServer.ControlPlane;

public readonly struct BackendPacketManifestId : IEquatable<BackendPacketManifestId>
{
    public const int MaxLength = 128;

    public BackendPacketManifestId(string value)
    {
        Value = Normalize(value);
    }

    public string Value { get; }

    public bool Equals(BackendPacketManifestId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is BackendPacketManifestId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool operator ==(BackendPacketManifestId left, BackendPacketManifestId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BackendPacketManifestId left, BackendPacketManifestId right)
    {
        return !left.Equals(right);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Backend packet manifest id is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"Backend packet manifest id must be {MaxLength} characters or fewer.", nameof(value));
        }

        foreach (var c in normalized)
        {
            if (!IsAllowedCharacter(c))
            {
                throw new ArgumentException("Backend packet manifest id may contain only letters, digits, dots, dashes, underscores, and colons.", nameof(value));
            }
        }

        return normalized;
    }

    private static bool IsAllowedCharacter(char c)
    {
        return c is >= 'a' and <= 'z' ||
               c is >= 'A' and <= 'Z' ||
               c is >= '0' and <= '9' ||
               c is '.' or '-' or '_' or ':';
    }
}
