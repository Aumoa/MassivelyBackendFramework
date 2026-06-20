using System;

namespace GatewayServer.Protocols;

public readonly struct GatewayBackendExchangeId : IEquatable<GatewayBackendExchangeId>
{
    public GatewayBackendExchangeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Exchange id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool Equals(GatewayBackendExchangeId other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is GatewayBackendExchangeId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }

    public static bool operator ==(GatewayBackendExchangeId left, GatewayBackendExchangeId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GatewayBackendExchangeId left, GatewayBackendExchangeId right)
    {
        return !left.Equals(right);
    }
}
