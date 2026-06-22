using System;

namespace BackendServer.Runtime;

public sealed class BackendGatewayChannelOpenContext
{
    public BackendGatewayChannelOpenContext(
        string gatewayNodeId,
        Guid gatewayConnectionId,
        uint channelId,
        string? principalSubjectId,
        DateTimeOffset openedAt)
    {
        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        if (gatewayConnectionId == Guid.Empty)
        {
            throw new ArgumentException("Gateway connection id is required.", nameof(gatewayConnectionId));
        }

        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (principalSubjectId != null && string.IsNullOrWhiteSpace(principalSubjectId))
        {
            throw new ArgumentException("Principal subject id cannot be blank.", nameof(principalSubjectId));
        }

        GatewayNodeId = gatewayNodeId;
        GatewayConnectionId = gatewayConnectionId;
        ChannelId = channelId;
        PrincipalSubjectId = principalSubjectId;
        OpenedAt = openedAt;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public BackendGatewayChannel Channel => new(GatewayConnectionId, ChannelId);

    public string? PrincipalSubjectId { get; }

    public DateTimeOffset OpenedAt { get; }
}
