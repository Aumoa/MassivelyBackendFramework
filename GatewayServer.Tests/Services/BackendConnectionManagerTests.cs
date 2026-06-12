using GatewayServer.Options;
using GatewayServer.Services;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class BackendConnectionManagerTests
{
    [Fact]
    public async Task StartAsync_AppliesBackendSnapshot_ByKind()
    {
        var catalog = new FakeBackendNodeCatalog(CreateSnapshot(
            CreateNode("beta", "backend-b", "master-b"),
            CreateNode("alpha", "backend-a", "master-a")));
        var manager = CreateManager(catalog);

        await manager.StartAsync(CancellationToken.None);

        Assert.Equal(["alpha", "beta"], manager.GetDiscoveredBackendKinds());

        var status = manager.GetStatusItems();
        Assert.Contains(status, item =>
            item.Group == "Backend" &&
            item.Name == "Discovered nodes" &&
            item.Value == "2");
        Assert.Contains(status, item =>
            item.Group == "Backend kind alpha" &&
            item.Name == "Discovered nodes" &&
            item.Value == "1");
        Assert.Contains(status, item =>
            item.Group == "Backend kind beta" &&
            item.Name == "Discovered nodes" &&
            item.Value == "1");
    }

    [Fact]
    public async Task SnapshotChanged_RemovesUnavailableKinds()
    {
        var catalog = new FakeBackendNodeCatalog(CreateSnapshot(
            CreateNode("alpha", "backend-a", "master-a")));
        var manager = CreateManager(catalog);
        await manager.StartAsync(CancellationToken.None);

        catalog.Publish(CreateSnapshot());

        Assert.Empty(manager.GetDiscoveredBackendKinds());
        Assert.Contains(manager.GetStatusItems(), item =>
            item.Group == "Backend" &&
            item.Name == "Discovered nodes" &&
            item.Value == "0");
    }

    [Fact]
    public async Task ConnectAsync_RejectsUnknownBackendKind()
    {
        var catalog = new FakeBackendNodeCatalog(CreateSnapshot());
        var manager = CreateManager(catalog);
        await manager.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ConnectAsync("missing", CancellationToken.None));
    }

    private static BackendConnectionManager CreateManager(FakeBackendNodeCatalog catalog)
    {
        return new BackendConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new BackendConnectionOptions()),
            Microsoft.Extensions.Options.Options.Create(new MasterConnectionOptions
            {
                NodeId = "gateway-test",
                DisplayName = "Gateway Test"
            }),
            catalog,
            new FakeDirectConnectCodeIssuer(),
            new TestLogger<BackendConnectionManager>());
    }

    private static BackendNodeSnapshot CreateSnapshot(params BackendNodeEndpoint[] nodes)
    {
        return new BackendNodeSnapshot(nodes, DateTimeOffset.UtcNow);
    }

    private static BackendNodeEndpoint CreateNode(
        string backendKind,
        string nodeId,
        string masterConnectionId)
    {
        return new BackendNodeEndpoint(
            backendKind,
            nodeId,
            displayName: nodeId,
            masterConnectionId,
            new MasterSocketEndpoint("127.0.0.1", 18000, useTls: false),
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeBackendNodeCatalog(BackendNodeSnapshot snapshot) : IBackendNodeCatalog
    {
        private BackendNodeSnapshot m_Snapshot = snapshot;

        public event Action<BackendNodeSnapshot>? SnapshotChanged;

        public BackendNodeSnapshot GetSnapshot()
        {
            return m_Snapshot;
        }

        public void Publish(BackendNodeSnapshot snapshot)
        {
            m_Snapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private sealed class FakeDirectConnectCodeIssuer : IDirectConnectCodeIssuer
    {
        public Task<DirectConnectCodeResponse> RequestDirectConnectCodeAsync(
            MasterNodeKind targetNodeKind,
            string targetMasterConnectionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("These tests do not open Backend sockets.");
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
