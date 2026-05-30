using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IDedicatedNodeCatalog
{
    event Action<DedicatedNodeSnapshot>? SnapshotChanged;

    DedicatedNodeSnapshot GetSnapshot();
}

internal interface IDedicatedNodeCatalogWriter
{
    void Publish(DedicatedNodeSnapshot snapshot);
}

internal sealed class DedicatedNodeCatalog : IDedicatedNodeCatalog, IDedicatedNodeCatalogWriter
{
    private readonly object m_Sync = new();
    private DedicatedNodeSnapshot m_Snapshot = new([], DateTimeOffset.UtcNow);

    public event Action<DedicatedNodeSnapshot>? SnapshotChanged;

    public DedicatedNodeSnapshot GetSnapshot()
    {
        lock (m_Sync)
        {
            return m_Snapshot;
        }
    }

    public void Publish(DedicatedNodeSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (m_Sync)
        {
            m_Snapshot = snapshot;
        }

        SnapshotChanged?.Invoke(snapshot);
    }
}
