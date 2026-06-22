using System;
using PacketCore;

namespace BackendServer.Runtime;

public sealed class BackendGatewayPacketContext
{
    public BackendGatewayPacketContext(
        string gatewayNodeId,
        Guid gatewayConnectionId,
        uint channelId,
        PacketKind kind,
        ushort packetId,
        ushort version,
        DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        GatewayNodeId = gatewayNodeId;
        GatewayConnectionId = gatewayConnectionId;
        ChannelId = channelId;
        Kind = kind;
        PacketId = packetId;
        Version = version;
        ReceivedAt = receivedAt;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public PacketKind Kind { get; }

    public ushort PacketId { get; }

    public ushort Version { get; }

    public DateTimeOffset ReceivedAt { get; }
}
