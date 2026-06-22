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
        DateTimeOffset receivedAt,
        Guid? exchangeId = null)
    {
        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (exchangeId == Guid.Empty)
        {
            throw new ArgumentException("Exchange id cannot be empty.", nameof(exchangeId));
        }

        if (kind is (PacketKind.Request or PacketKind.Response) && exchangeId == null)
        {
            throw new ArgumentException("Request and response packets require an exchange id.", nameof(exchangeId));
        }

        GatewayNodeId = gatewayNodeId;
        GatewayConnectionId = gatewayConnectionId;
        ChannelId = channelId;
        Kind = kind;
        PacketId = packetId;
        Version = version;
        ReceivedAt = receivedAt;
        ExchangeId = exchangeId;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public BackendGatewayChannel Channel => new(GatewayConnectionId, ChannelId);

    public PacketKind Kind { get; }

    public ushort PacketId { get; }

    public ushort Version { get; }

    public Guid? ExchangeId { get; }

    public DateTimeOffset ReceivedAt { get; }
}
