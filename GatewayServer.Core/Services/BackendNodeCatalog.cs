using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IBackendNodeCatalog
{
    event Action<BackendNodeSnapshot>? SnapshotChanged;

    BackendNodeSnapshot GetSnapshot();
}

internal interface IBackendNodeCatalogWriter
{
    void Publish(BackendNodeSnapshot snapshot);
}

internal sealed class BackendNodeCatalog : IBackendNodeCatalog, IBackendNodeCatalogWriter
{
    private readonly object m_Sync = new();
    private BackendNodeSnapshot m_Snapshot = new([], DateTimeOffset.UtcNow);

    public event Action<BackendNodeSnapshot>? SnapshotChanged;

    public BackendNodeSnapshot GetSnapshot()
    {
        lock (m_Sync)
        {
            return m_Snapshot;
        }
    }

    public void Publish(BackendNodeSnapshot snapshot)
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
