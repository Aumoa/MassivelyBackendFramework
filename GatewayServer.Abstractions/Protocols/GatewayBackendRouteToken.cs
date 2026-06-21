using System;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteToken : IEquatable<GatewayBackendRouteToken>
{
    public const int MaxLength = 512;

    public GatewayBackendRouteToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Route token is required.", nameof(value));
        }

        if (value.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (HasWhitespace(value))
        {
            throw new ArgumentException("Route token must not contain whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public bool Equals(GatewayBackendRouteToken? other)
    {
        return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as GatewayBackendRouteToken);
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
