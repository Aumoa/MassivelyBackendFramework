using System;

namespace MasterServer.ControlPlane;

public sealed class MasterConnectionSnapshot
{
    public MasterConnectionSnapshot(
        Guid connectionId,
        string remoteEndPoint,
        MasterNodeKind nodeKind,
        DateTimeOffset connectedAt,
        DateTimeOffset lastSeenAt)
    {
        ConnectionId = connectionId;
        RemoteEndPoint = remoteEndPoint;
        NodeKind = nodeKind;
        ConnectedAt = connectedAt;
        LastSeenAt = lastSeenAt;
    }

    public Guid ConnectionId { get; }

    public string RemoteEndPoint { get; }

    public MasterNodeKind NodeKind { get; }

    public DateTimeOffset ConnectedAt { get; }

    public DateTimeOffset LastSeenAt { get; }
}
