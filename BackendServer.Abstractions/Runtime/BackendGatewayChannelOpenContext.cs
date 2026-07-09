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
        : this(
            gatewayNodeId,
            gatewayConnectionId,
            channelId,
            principalSubjectId,
            principalAuthenticationMethodKind: null,
            principalAuthenticationMethodId: null,
            openedAt)
    {
    }

    public BackendGatewayChannelOpenContext(
        string gatewayNodeId,
        Guid gatewayConnectionId,
        uint channelId,
        string? principalSubjectId,
        byte? principalAuthenticationMethodKind,
        string? principalAuthenticationMethodId,
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

        if (principalAuthenticationMethodId != null && string.IsNullOrWhiteSpace(principalAuthenticationMethodId))
        {
            throw new ArgumentException("Principal authentication method id cannot be blank.", nameof(principalAuthenticationMethodId));
        }

        if (principalAuthenticationMethodKind.HasValue != (principalAuthenticationMethodId != null))
        {
            throw new ArgumentException("Principal authentication method kind and id must be provided together.");
        }

        if (principalSubjectId == null &&
            (principalAuthenticationMethodKind.HasValue || principalAuthenticationMethodId != null))
        {
            throw new ArgumentException("Principal authentication method cannot be provided without a principal subject id.");
        }

        GatewayNodeId = gatewayNodeId;
        GatewayConnectionId = gatewayConnectionId;
        ChannelId = channelId;
        PrincipalSubjectId = principalSubjectId;
        PrincipalAuthenticationMethodKind = principalAuthenticationMethodKind;
        PrincipalAuthenticationMethodId = principalAuthenticationMethodId;
        OpenedAt = openedAt;
    }

    public string GatewayNodeId { get; }

    public Guid GatewayConnectionId { get; }

    public uint ChannelId { get; }

    public BackendGatewayChannel Channel => new(GatewayConnectionId, ChannelId);

    public string? PrincipalSubjectId { get; }

    public byte? PrincipalAuthenticationMethodKind { get; }

    public string? PrincipalAuthenticationMethodId { get; }

    public DateTimeOffset OpenedAt { get; }
}
