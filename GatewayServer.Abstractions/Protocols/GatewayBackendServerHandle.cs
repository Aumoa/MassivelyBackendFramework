using System;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendServerHandle : IEquatable<GatewayBackendServerHandle>
{
    public const int MaxLength = 128;

    public GatewayBackendServerHandle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Server handle is required.", nameof(value));
        }

        if (value.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (HasWhitespace(value))
        {
            throw new ArgumentException("Server handle must not contain whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public bool Equals(GatewayBackendServerHandle? other)
    {
        return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as GatewayBackendServerHandle);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool HasWhitespace(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return true;
            }
        }

        return false;
    }
}
