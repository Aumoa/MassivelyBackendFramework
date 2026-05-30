using System;
using PacketCore;

namespace DedicatedServer.Runtime;

public sealed class DedicatedGatewayPacketContext
{
    public DedicatedGatewayPacketContext(
        string gatewayNodeId,
        Guid gatewayConnectionId,
        PacketKind kind,
        ushort packetId,
        ushort version,
        DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        GatewayNodeId = gatewayNodeId;
        GatewayConnectionId = gatewayConnectionId;
        Kind = kind;
        PacketId = packetId;
        Version = version;
        ReceivedAt = receivedAt;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public PacketKind Kind { get; }

    public ushort PacketId { get; }

    public ushort Version { get; }

    public DateTimeOffset ReceivedAt { get; }
}
