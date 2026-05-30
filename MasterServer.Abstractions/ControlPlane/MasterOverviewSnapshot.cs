using System;

namespace MasterServer.ControlPlane;

public sealed class MasterOverviewSnapshot
{
    public MasterOverviewSnapshot(
        MasterSocketEndpoint socketEndpoint,
        MasterConnectionSnapshot[] connections,
        DateTimeOffset observedAt)
    {
        SocketEndpoint = socketEndpoint;
        Connections = connections;
        ObservedAt = observedAt;
    }

    public MasterSocketEndpoint SocketEndpoint { get; }

    public MasterConnectionSnapshot[] Connections { get; }

    public DateTimeOffset ObservedAt { get; }
}
