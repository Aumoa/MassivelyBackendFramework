using System.Net;
using System.Net.Sockets;
using GatewayServer.Options;
using GatewayServer.Protocols;
using GatewayServer.Services;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;
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

    [Fact]
    public async Task RelayFrameAsync_CompletesDirectHandshakeAndRaisesRouteFrames()
    {
        var responseRouteId = Guid.NewGuid();
        var responsePayload = new byte[] { 9, 8, 7 };
        var responseEnvelope = new GatewayBackendRouteEnvelope(
            "alpha",
            responseRouteId,
            PacketKind.Response,
            routedPacketId: 205,
            routedVersion: 3,
            responsePayload);
        await using var backend = new FakeBackendServer(responseEnvelope);

        var issuer = new RecordingDirectConnectCodeIssuer("direct-code");
        var catalog = new FakeBackendNodeCatalog(CreateSnapshot(
            CreateNode("alpha", "backend-a", "master-a", backend.Port)));
        var manager = CreateManager(catalog, issuer);
        manager.SetMasterConnectionId("gateway-master-a");
        await manager.StartAsync(CancellationToken.None);

        var receivedFrame = new TaskCompletionSource<BackendRouteFrameReceived>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        manager.RouteFrameReceived += (frame, _) =>
        {
            receivedFrame.TrySetResult(frame);
            return ValueTask.CompletedTask;
        };

        var requestRouteId = Guid.NewGuid();
        var requestPayload = new byte[] { 1, 2, 3, 4 };
        using var requestFrame = CreateBackendRouteFrame(
            "alpha",
            requestRouteId,
            PacketKind.Request,
            routedPacketId: 101,
            routedVersion: 2,
            requestPayload);

        try
        {
            await manager.RelayFrameAsync("alpha", requestFrame, CancellationToken.None);

            var handshake = await backend.Handshake.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(MasterNodeKind.Gateway, handshake.Hello.NodeKind);
            Assert.Equal("gateway-test", handshake.Hello.NodeId);
            Assert.Equal("Gateway Test", handshake.Hello.DisplayName);
            Assert.Equal(MasterControlProtocol.SchemaVersion, handshake.Hello.ProtocolVersion);
            Assert.Equal("gateway-master-a", handshake.Hello.MasterConnectionId);
            Assert.Equal("direct-code", handshake.DirectConnectCode.Code);

            var request = Assert.Single(issuer.Requests);
            Assert.Equal(MasterNodeKind.Backend, request.TargetNodeKind);
            Assert.Equal("master-a", request.TargetMasterConnectionId);

            var backendRelay = await backend.RelayedEnvelope.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", backendRelay.BackendKind);
            Assert.Equal(requestRouteId, backendRelay.RouteId);
            Assert.Equal(PacketKind.Request, backendRelay.RoutedKind);
            Assert.Equal((ushort)101, backendRelay.RoutedPacketId);
            Assert.Equal((ushort)2, backendRelay.RoutedVersion);
            Assert.Equal(requestPayload, backendRelay.RoutedPayload);

            var routedBack = await receivedFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", routedBack.BackendKind);
            Assert.Equal("backend-a", routedBack.NodeId);
            Assert.Equal("master-a", routedBack.MasterConnectionId);
            Assert.Equal(responseRouteId, routedBack.Envelope.RouteId);
            Assert.Equal(PacketKind.Response, routedBack.Envelope.RoutedKind);
            Assert.Equal((ushort)205, routedBack.Envelope.RoutedPacketId);
            Assert.Equal((ushort)3, routedBack.Envelope.RoutedVersion);
            Assert.Equal(responsePayload, routedBack.Envelope.RoutedPayload);

            var status = manager.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Backend" &&
                item.Name == "Active sessions" &&
                item.Value == "1");
            Assert.Contains(status, item =>
                item.Group == "Backend alpha/backend-a" &&
                item.Name == "State" &&
                item.Value == "Trusted");
            Assert.Contains(status, item =>
                item.Group == "Backend alpha/backend-a" &&
                item.Name == "Direct connection" &&
                item.Value == FakeBackendServer.DirectConnectionId);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConnectAsync_RejectsAcceptedNodeIdMismatch()
    {
        await using var backend = new FakeBackendServer(
            responseEnvelope: null,
            acceptedNodeId: "different-gateway",
            expectRelay: false);

        var issuer = new RecordingDirectConnectCodeIssuer("direct-code");
        var catalog = new FakeBackendNodeCatalog(CreateSnapshot(
            CreateNode("alpha", "backend-a", "master-a", backend.Port)));
        var manager = CreateManager(catalog, issuer);
        manager.SetMasterConnectionId("gateway-master-a");
        await manager.StartAsync(CancellationToken.None);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ConnectAsync("alpha", CancellationToken.None));

            var handshake = await backend.Handshake.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("gateway-test", handshake.Hello.NodeId);

            var request = Assert.Single(issuer.Requests);
            Assert.Equal(MasterNodeKind.Backend, request.TargetNodeKind);
            Assert.Equal("master-a", request.TargetMasterConnectionId);

            Assert.Contains(manager.GetStatusItems(), item =>
                item.Group == "Backend alpha/backend-a" &&
                item.Name == "State" &&
                item.Value == "Connection failed");
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    private static BackendConnectionManager CreateManager(FakeBackendNodeCatalog catalog)
    {
        return CreateManager(catalog, new FakeDirectConnectCodeIssuer());
    }

    private static BackendConnectionManager CreateManager(
        FakeBackendNodeCatalog catalog,
        IDirectConnectCodeIssuer directConnectCodeIssuer)
    {
        return new BackendConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new BackendConnectionOptions()),
            Microsoft.Extensions.Options.Options.Create(new MasterConnectionOptions
            {
                NodeId = "gateway-test",
                DisplayName = "Gateway Test"
            }),
            catalog,
            directConnectCodeIssuer,
            new TestLogger<BackendConnectionManager>());
    }

    private static BackendNodeSnapshot CreateSnapshot(params BackendNodeEndpoint[] nodes)
    {
        return new BackendNodeSnapshot(nodes, DateTimeOffset.UtcNow);
    }

    private static BackendNodeEndpoint CreateNode(
        string backendKind,
        string nodeId,
        string masterConnectionId,
        int port = 18000)
    {
        return new BackendNodeEndpoint(
            backendKind,
            nodeId,
            displayName: nodeId,
            masterConnectionId,
            new MasterSocketEndpoint("127.0.0.1", port, useTls: false),
            DateTimeOffset.UtcNow);
    }

    private static PacketFrame CreateBackendRouteFrame(
        string backendKind,
        Guid routeId,
        PacketKind routedKind,
        ushort routedPacketId,
        ushort routedVersion,
        byte[] payload)
    {
        var envelope = new GatewayBackendRouteEnvelope(
            backendKind,
            routeId,
            routedKind,
            routedPacketId,
            routedVersion,
            payload);
        return PacketCodec.Encode(
            routedKind,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteEnvelope.Codec);
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

    private sealed class RecordingDirectConnectCodeIssuer(string code) : IDirectConnectCodeIssuer
    {
        private readonly List<DirectConnectCodeRequestRecord> m_Requests = [];

        public DirectConnectCodeRequestRecord[] Requests
        {
            get
            {
                lock (m_Requests)
                {
                    return [.. m_Requests];
                }
            }
        }

        public Task<DirectConnectCodeResponse> RequestDirectConnectCodeAsync(
            MasterNodeKind targetNodeKind,
            string targetMasterConnectionId,
            CancellationToken cancellationToken)
        {
            lock (m_Requests)
            {
                m_Requests.Add(new DirectConnectCodeRequestRecord(targetNodeKind, targetMasterConnectionId));
            }

            return Task.FromResult(new DirectConnectCodeResponse(
                Guid.NewGuid(),
                success: true,
                code,
                DateTimeOffset.UtcNow.AddMinutes(1),
                errorMessage: string.Empty));
        }
    }

    private sealed record DirectConnectCodeRequestRecord(
        MasterNodeKind TargetNodeKind,
        string TargetMasterConnectionId);

    private sealed class FakeBackendServer : IAsyncDisposable
    {
        public const string DirectConnectionId = "backend-direct-a";

        private readonly TcpListener m_Listener;
        private readonly CancellationTokenSource m_Cancellation = new();
        private readonly GatewayBackendRouteEnvelope? m_ResponseEnvelope;
        private readonly string? m_AcceptedNodeId;
        private readonly bool m_ExpectRelay;
        private readonly Task m_RunTask;

        public FakeBackendServer(
            GatewayBackendRouteEnvelope? responseEnvelope,
            string? acceptedNodeId = null,
            bool expectRelay = true)
        {
            m_ResponseEnvelope = responseEnvelope;
            m_AcceptedNodeId = acceptedNodeId;
            m_ExpectRelay = expectRelay;
            m_Listener = new TcpListener(IPAddress.Loopback, 0);
            m_Listener.Start();
            Port = ((IPEndPoint)m_Listener.LocalEndpoint).Port;
            m_RunTask = RunAsync(m_Cancellation.Token);
        }

        public int Port { get; }

        public TaskCompletionSource<ObservedBackendHandshake> Handshake { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<GatewayBackendRouteEnvelope> RelayedEnvelope { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            await m_Cancellation.CancelAsync();
            m_Listener.Stop();

            try
            {
                await m_RunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }

            m_Cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var tcpClient = await m_Listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = tcpClient.GetStream();

                await WriteControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeAuthChallenge,
                    new NodeAuthChallenge("challenge-a", new byte[MasterControlProtocol.AuthNonceLength]),
                    NodeAuthChallenge.Codec,
                    cancellationToken);

                var hello = await ReadControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeHello,
                    NodeHello.Codec,
                    cancellationToken);
                var directConnectCode = await ReadControlFrameAsync(
                    stream,
                    MasterControlPacketIds.DirectConnectCode,
                    DirectConnectCode.Codec,
                    cancellationToken);
                Handshake.TrySetResult(new ObservedBackendHandshake(hello, directConnectCode));

                await WriteControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeAccepted,
                    new NodeAccepted(m_AcceptedNodeId ?? hello.NodeId, DirectConnectionId),
                    NodeAccepted.Codec,
                    cancellationToken);

                if (!m_ExpectRelay)
                {
                    await WaitUntilCancelledAsync(cancellationToken);
                    return;
                }

                using var relayedFrame = await ReadRequiredFrameAsync(
                    stream,
                    MasterControlProtocol.TrustedControlPlanePolicy,
                    cancellationToken);
                if (relayedFrame.Header.PacketId != Pid.GATE_BACKEND_ROUTE ||
                    relayedFrame.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
                {
                    throw new InvalidOperationException("Gateway relayed an unexpected Backend route frame.");
                }

                var relayedEnvelope = PacketCodec.Decode(relayedFrame, GatewayBackendRouteEnvelope.Codec);
                RelayedEnvelope.TrySetResult(relayedEnvelope);

                if (m_ResponseEnvelope != null)
                {
                    using var responseFrame = PacketCodec.Encode(
                        PacketKind.Response,
                        Pid.GATE_BACKEND_ROUTE,
                        GatewayBackendRouteEnvelope.ProtocolVersion,
                        m_ResponseEnvelope,
                        GatewayBackendRouteEnvelope.Codec);
                    await PacketFrameWriter.WriteAsync(stream, responseFrame, cancellationToken);
                }

                await WaitUntilCancelledAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Handshake.TrySetException(e);
                RelayedEnvelope.TrySetException(e);
            }
        }

        private static async Task<TPacket> ReadControlFrameAsync<TPacket>(
            Stream stream,
            ushort packetId,
            IPacketCodec<TPacket> codec,
            CancellationToken cancellationToken)
        {
            using var frame = await ReadRequiredFrameAsync(
                stream,
                MasterControlProtocol.UntrustedHandshakePolicy,
                cancellationToken);
            MasterControlProtocol.ValidateControlFrame(frame, packetId);
            return PacketCodec.Decode(frame, codec);
        }

        private static async Task<PacketFrame> ReadRequiredFrameAsync(
            Stream stream,
            PacketReadPolicy policy,
            CancellationToken cancellationToken)
        {
            var frame = await PacketFrameReader.ReadAsync(stream, policy, cancellationToken);
            return frame ?? throw new EndOfStreamException("Backend test connection closed.");
        }

        private static async Task WriteControlFrameAsync<TPacket>(
            Stream stream,
            ushort packetId,
            TPacket value,
            IPacketCodec<TPacket> codec,
            CancellationToken cancellationToken)
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                packetId,
                MasterControlProtocol.SchemaVersion,
                value,
                codec);
            await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken);
        }

        private static async Task WaitUntilCancelledAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private sealed record ObservedBackendHandshake(
        NodeHello Hello,
        DirectConnectCode DirectConnectCode);

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
