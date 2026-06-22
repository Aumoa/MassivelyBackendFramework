using System;

namespace BackendServer.Runtime;

public sealed class BackendGatewayChannelCloseContext
{
    public BackendGatewayChannelCloseContext(
        string gatewayNodeId,
        Guid gatewayConnectionId,
        uint channelId,
        string reason,
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
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        ReceivedAt = receivedAt;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public string Reason { get; }

    public DateTimeOffset ReceivedAt { get; }
}
