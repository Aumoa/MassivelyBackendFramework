using System;

namespace BackendServer.Runtime;

public readonly struct BackendGatewayChannel : IEquatable<BackendGatewayChannel>
{
    public BackendGatewayChannel(Guid gatewayConnectionId, uint channelId)
    {
        if (gatewayConnectionId == Guid.Empty)
        {
            throw new ArgumentException("Gateway connection id is required.", nameof(gatewayConnectionId));
        }

        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        GatewayConnectionId = gatewayConnectionId;
        ChannelId = channelId;
    }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public bool Equals(BackendGatewayChannel other)
    {
        return GatewayConnectionId.Equals(other.GatewayConnectionId) &&
               ChannelId == other.ChannelId;
    }

    public override bool Equals(object? obj)
    {
        return obj is BackendGatewayChannel other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GatewayConnectionId, ChannelId);
    }

    public static bool operator ==(BackendGatewayChannel left, BackendGatewayChannel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BackendGatewayChannel left, BackendGatewayChannel right)
    {
        return !left.Equals(right);
    }
}
