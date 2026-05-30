using MasterServer.ControlPlane;

namespace MasterAdmin.Services;

public sealed class MasterOverviewState
{
    public MasterOverviewState(
        MasterOverviewConnectionState connectionState,
        MasterOverviewSnapshot snapshot,
        string? lastError = null,
        DateTimeOffset? nextReconnectAt = null)
    {
        ConnectionState = connectionState;
        Snapshot = snapshot;
        LastError = lastError;
        NextReconnectAt = nextReconnectAt;
    }

    public MasterOverviewConnectionState ConnectionState { get; }

    public MasterOverviewSnapshot Snapshot { get; }

    public string? LastError { get; }

    public DateTimeOffset? NextReconnectAt { get; }
}
