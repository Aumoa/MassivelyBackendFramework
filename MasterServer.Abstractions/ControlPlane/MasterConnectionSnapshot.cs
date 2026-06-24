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
        : this(
            connectionId,
            remoteEndPoint,
            nodeKind,
            string.Empty,
            string.Empty,
            string.Empty,
            connectedAt,
            lastSeenAt)
    {
    }

    public MasterConnectionSnapshot(
        Guid connectionId,
        string remoteEndPoint,
        MasterNodeKind nodeKind,
        string nodeId,
        string displayName,
        string backendKind,
        DateTimeOffset connectedAt,
        DateTimeOffset lastSeenAt)
    {
        ConnectionId = connectionId;
        RemoteEndPoint = remoteEndPoint;
        NodeKind = nodeKind;
        NodeId = nodeId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        BackendKind = backendKind ?? string.Empty;
        ConnectedAt = connectedAt;
        LastSeenAt = lastSeenAt;
    }

    public Guid ConnectionId { get; }

    public string RemoteEndPoint { get; }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string BackendKind { get; }

    public DateTimeOffset ConnectedAt { get; }

    public DateTimeOffset LastSeenAt { get; }
}
