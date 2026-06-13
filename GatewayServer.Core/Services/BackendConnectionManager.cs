using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using GatewayServer.Options;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal interface IBackendRouteManager
{
    event BackendRouteFrameReceivedHandler? RouteFrameReceived;

    string[] GetDiscoveredBackendKinds();

    ValueTask<IBackendRouteSession> ConnectAsync(
        string backendKind,
        CancellationToken cancellationToken);

    ValueTask RelayFrameAsync(
        string backendKind,
        PacketFrame frame,
        CancellationToken cancellationToken);
}

internal interface IBackendRouteSession
{
    string BackendKind { get; }

    string NodeId { get; }

    string MasterConnectionId { get; }

    string DirectConnectionId { get; }

    ValueTask WriteAsync(PacketFrame frame, CancellationToken cancellationToken);
}

internal delegate ValueTask BackendRouteFrameReceivedHandler(
    BackendRouteFrameReceived frame,
    CancellationToken cancellationToken);

internal sealed class BackendRouteFrameReceived(
    string backendKind,
    string nodeId,
    string masterConnectionId,
    GatewayBackendRouteEnvelope envelope)
{
    public string BackendKind { get; } = backendKind;

    public string NodeId { get; } = nodeId;

    public string MasterConnectionId { get; } = masterConnectionId;

    public GatewayBackendRouteEnvelope Envelope { get; } = envelope;
}

internal interface IBackendConnectionStatusProvider
{
    ServiceAdminStatusItem[] GetStatusItems();
}

internal sealed class BackendConnectionManager(
    IOptions<BackendConnectionOptions> options,
    IOptions<MasterConnectionOptions> gatewayIdentity,
    IBackendNodeCatalog catalog,
    IDirectConnectCodeIssuer directConnectCodeIssuer,
    ILogger<BackendConnectionManager> logger) : IHostedService, IBackendRouteManager, IBackendConnectionStatusProvider, IGatewayMasterConnectionIdentitySink
{
    private readonly BackendConnectionOptions m_Options = options.Value;
    private readonly MasterConnectionOptions m_GatewayIdentity = gatewayIdentity.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_RoutesSync = new();
    private readonly Dictionary<string, BackendNodeEndpoint[]> m_NodesByKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackendPeer> m_Peers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> m_PeerStates = [];
    private readonly ConcurrentDictionary<string, string> m_PeerDirectConnectionIds = [];
    private string? m_MasterConnectionId;

    public event BackendRouteFrameReceivedHandler? RouteFrameReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            logger.LogInformation("Gateway Backend direct connections are disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        catalog.SnapshotChanged += OnBackendSnapshotChanged;
        ApplySnapshot(catalog.GetSnapshot());
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        catalog.SnapshotChanged -= OnBackendSnapshotChanged;
        await m_Shutdown.CancelAsync().ConfigureAwait(false);

        BackendPeer[] peers;
        lock (m_RoutesSync)
        {
            peers = [.. m_Peers.Values];
            m_Peers.Clear();
            m_NodesByKind.Clear();
        }

        foreach (var peer in peers)
        {
            await peer.Cancellation.CancelAsync().ConfigureAwait(false);
            await peer.DisposeSessionAsync().ConfigureAwait(false);
        }

        var peerTasks = peers
            .Select(static peer => peer.DrainTask)
            .Where(static task => task != null)
            .Select(static task => task!);
        await WaitForShutdownAsync(Task.WhenAll(peerTasks), cancellationToken).ConfigureAwait(false);
    }

    public string[] GetDiscoveredBackendKinds()
    {
        lock (m_RoutesSync)
        {
            return [.. m_NodesByKind.Keys.OrderBy(static backendKind => backendKind, StringComparer.Ordinal)];
        }
    }

    public async ValueTask<IBackendRouteSession> ConnectAsync(
        string backendKind,
        CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            throw new InvalidOperationException("Gateway Backend direct connections are disabled.");
        }

        var node = SelectBackendNode(backendKind);
        var peer = GetOrCreatePeer(node);
        return await ConnectPeerAsync(peer, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RelayFrameAsync(
        string backendKind,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var session = await ConnectAsync(backendKind, cancellationToken).ConfigureAwait(false);
        await session.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        BackendNodeEndpoint[] nodes;
        BackendPeer[] peers;

        lock (m_RoutesSync)
        {
            nodes = [.. m_NodesByKind.Values.SelectMany(static item => item)];
            peers = [.. m_Peers.Values];
        }

        var items = new List<ServiceAdminStatusItem>
        {
            new("Backend", "Configured", m_Options.Enabled ? "Enabled" : "Disabled"),
            new("Backend", "Discovered kinds", nodes
                .Select(static node => node.BackendKind)
                .Distinct(StringComparer.Ordinal)
                .Count()
                .ToString()),
            new("Backend", "Discovered nodes", nodes.Length.ToString()),
            new("Backend", "Active sessions", peers.Count(static peer => peer.CurrentSession != null).ToString())
        };

        foreach (var group in nodes
                     .GroupBy(static node => node.BackendKind, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new ServiceAdminStatusItem($"Backend kind {group.Key}", "Discovered nodes", group.Count().ToString()));
        }

        foreach (var peer in peers.OrderBy(static peer => peer.Node.BackendKind, StringComparer.Ordinal)
                     .ThenBy(static peer => peer.Node.NodeId, StringComparer.Ordinal))
        {
            var state = m_PeerStates.TryGetValue(peer.Node.MasterConnectionId, out var peerState)
                ? peerState
                : "Unknown";
            var group = $"Backend {peer.Node.BackendKind}/{peer.Node.NodeId}";
            items.Add(new ServiceAdminStatusItem(group, "Backend kind", peer.Node.BackendKind));
            items.Add(new ServiceAdminStatusItem(group, "Node", peer.Node.NodeId));
            items.Add(new ServiceAdminStatusItem(group, "Display name", peer.Node.DisplayName));
            items.Add(new ServiceAdminStatusItem(group, "State", state));
            items.Add(new ServiceAdminStatusItem(group, "Endpoint", $"{peer.Node.GatewayEndpoint.IPAddress}:{peer.Node.GatewayEndpoint.Port}"));
            items.Add(new ServiceAdminStatusItem(group, "TLS", peer.Node.GatewayEndpoint.UseTls ? "Enabled" : "Disabled"));
            items.Add(new ServiceAdminStatusItem(group, "Master connection", peer.Node.MasterConnectionId));
            if (m_PeerDirectConnectionIds.TryGetValue(peer.Node.MasterConnectionId, out var directConnectionId))
            {
                items.Add(new ServiceAdminStatusItem(group, "Direct connection", directConnectionId));
            }
        }

        return [.. items];
    }

    public void SetMasterConnectionId(string? masterConnectionId)
    {
        m_MasterConnectionId = string.IsNullOrWhiteSpace(masterConnectionId)
            ? null
            : masterConnectionId;
    }

    private void OnBackendSnapshotChanged(BackendNodeSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(BackendNodeSnapshot snapshot)
    {
        var desired = snapshot.Nodes
            .GroupBy(static node => node.MasterConnectionId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToDictionary(static node => node.MasterConnectionId, StringComparer.Ordinal);
        var nodesByKind = desired.Values
            .GroupBy(static node => node.BackendKind, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static node => node.NodeId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        List<BackendPeer> removed = [];

        lock (m_RoutesSync)
        {
            m_NodesByKind.Clear();
            foreach (var pair in nodesByKind)
            {
                m_NodesByKind[pair.Key] = pair.Value;
            }

            foreach (var current in m_Peers.Values.ToArray())
            {
                if (!desired.TryGetValue(current.Node.MasterConnectionId, out var next) ||
                    !HasSameEndpoint(current.Node, next) ||
                    !string.Equals(current.Node.BackendKind, next.BackendKind, StringComparison.Ordinal))
                {
                    m_Peers.Remove(current.Node.MasterConnectionId);
                    m_PeerStates.TryRemove(current.Node.MasterConnectionId, out _);
                    m_PeerDirectConnectionIds.TryRemove(current.Node.MasterConnectionId, out _);
                    removed.Add(current);
                }
            }
        }

        foreach (var peer in removed)
        {
            _ = peer.Cancellation.CancelAsync();
            _ = peer.DisposeSessionAsync();
        }
    }

    private BackendNodeEndpoint SelectBackendNode(string backendKind)
    {
        var normalizedBackendKind = NormalizeBackendKind(backendKind);

        lock (m_RoutesSync)
        {
            if (!m_NodesByKind.TryGetValue(normalizedBackendKind, out var nodes) ||
                nodes.Length == 0)
            {
                throw new InvalidOperationException($"No Backend nodes are available for kind '{normalizedBackendKind}'.");
            }

            return nodes.FirstOrDefault(node => m_Peers.ContainsKey(node.MasterConnectionId)) ?? nodes[0];
        }
    }

    private BackendPeer GetOrCreatePeer(BackendNodeEndpoint node)
    {
        lock (m_RoutesSync)
        {
            if (m_Peers.TryGetValue(node.MasterConnectionId, out var peer))
            {
                return peer;
            }

            var created = new BackendPeer(
                node,
                CancellationTokenSource.CreateLinkedTokenSource(m_Shutdown.Token));
            m_Peers[node.MasterConnectionId] = created;
            m_PeerStates[node.MasterConnectionId] = "Discovered";
            return created;
        }
    }

    private async ValueTask<BackendSession> ConnectPeerAsync(
        BackendPeer peer,
        CancellationToken cancellationToken)
    {
        await peer.ConnectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (peer.CurrentSession is { IsConnected: true } currentSession)
            {
                return currentSession;
            }

            await peer.DisposeSessionAsync().ConfigureAwait(false);
            m_PeerStates[peer.Node.MasterConnectionId] = "Connecting";

            using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                peer.Cancellation.Token,
                cancellationToken);
            try
            {
                var session = await OpenSessionAsync(peer.Node, connectCancellation.Token).ConfigureAwait(false);
                peer.CurrentSession = session;
                peer.DrainTask = DrainBackendFramesAsync(peer, session, peer.Cancellation.Token);
                return session;
            }
            catch (OperationCanceledException) when (peer.Cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException e)
            {
                m_PeerStates[peer.Node.MasterConnectionId] = "Handshake timeout";
                m_PeerDirectConnectionIds.TryRemove(peer.Node.MasterConnectionId, out _);
                logger.LogWarning(
                    "Backend direct handshake timed out. BackendKind={BackendKind}, BackendNodeId={NodeId}, Message={Message}.",
                    peer.Node.BackendKind,
                    peer.Node.NodeId,
                    e.Message);
                throw;
            }
            catch (Exception e)
            {
                m_PeerStates[peer.Node.MasterConnectionId] = "Connection failed";
                m_PeerDirectConnectionIds.TryRemove(peer.Node.MasterConnectionId, out _);
                logger.LogWarning(
                    e,
                    "Backend direct connection failed. BackendKind={BackendKind}, BackendNodeId={NodeId}, Endpoint={Address}:{Port}.",
                    peer.Node.BackendKind,
                    peer.Node.NodeId,
                    peer.Node.GatewayEndpoint.IPAddress,
                    peer.Node.GatewayEndpoint.Port);
                throw;
            }
        }
        finally
        {
            peer.ConnectLock.Release();
        }
    }

    private async ValueTask<BackendSession> OpenSessionAsync(
        BackendNodeEndpoint node,
        CancellationToken cancellationToken)
    {
        var endpoint = node.GatewayEndpoint;
        var socket = await MasterEndpointResolver.ConnectTcpAsync(
            endpoint.IPAddress,
            endpoint.Port,
            cancellationToken).ConfigureAwait(false);
        m_PeerStates[node.MasterConnectionId] = "Connected";
        logger.LogInformation(
            "Gateway connected to Backend node. BackendKind={BackendKind}, BackendNodeId={NodeId}, Endpoint={Address}:{Port}.",
            node.BackendKind,
            node.NodeId,
            endpoint.IPAddress,
            endpoint.Port);

        NetworkStream? networkStream = null;
        SslStream? sslStream = null;

        try
        {
            networkStream = new NetworkStream(socket, ownsSocket: true);
            Stream activeStream = networkStream;

            if (endpoint.UseTls)
            {
                sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsClientAsync(
                    m_Options.ServerName,
                    clientCertificates: null,
                    enabledSslProtocols: SslProtocols.Tls13,
                    checkCertificateRevocation: true).ConfigureAwait(false);
                activeStream = sslStream;
            }

            var accepted = await CompleteHandshakeAsync(activeStream, node, cancellationToken).ConfigureAwait(false);
            m_PeerDirectConnectionIds[node.MasterConnectionId] = accepted.ConnectionId;
            m_PeerStates[node.MasterConnectionId] = "Trusted";
            logger.LogInformation(
                "Gateway Backend direct session trusted. BackendKind={BackendKind}, BackendNodeId={BackendNodeId}, GatewayNodeId={GatewayNodeId}, BackendConnectionId={ConnectionId}.",
                node.BackendKind,
                node.NodeId,
                accepted.NodeId,
                accepted.ConnectionId);

            return new BackendSession(
                node.BackendKind,
                node.NodeId,
                node.MasterConnectionId,
                accepted.ConnectionId,
                activeStream,
                socket);
        }
        catch
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
            else if (networkStream != null)
            {
                await networkStream.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }

    private async Task<NodeAccepted> CompleteHandshakeAsync(
        Stream stream,
        BackendNodeEndpoint node,
        CancellationToken cancellationToken)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, m_Options.HandshakeTimeoutMilliseconds)));

        var handshakeStep = "NodeAuthChallenge";

        try
        {
            using var challengeFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAuthChallenge,
                handshakeTimeout.Token).ConfigureAwait(false);
            PacketCodec.Decode(challengeFrame, NodeAuthChallenge.Codec);

            var masterConnectionId = m_MasterConnectionId;
            if (string.IsNullOrWhiteSpace(masterConnectionId))
            {
                throw new InvalidOperationException("Gateway Master connection id is required before connecting to Backend nodes.");
            }

            handshakeStep = "DirectConnectCodeRequest";
            var directConnectCode = await directConnectCodeIssuer
                .RequestDirectConnectCodeAsync(
                    MasterNodeKind.Backend,
                    node.MasterConnectionId,
                    handshakeTimeout.Token)
                .ConfigureAwait(false);

            var hello = new NodeHello(
                MasterNodeKind.Gateway,
                m_GatewayIdentity.NodeId,
                m_GatewayIdentity.DisplayName,
                MasterControlProtocol.SchemaVersion,
                masterConnectionId);

            handshakeStep = "NodeHello";
            using (var helloFrame = PacketCodec.Encode(
                       PacketKind.Control,
                       MasterControlPacketIds.NodeHello,
                       MasterControlProtocol.SchemaVersion,
                       hello,
                       NodeHello.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, helloFrame, handshakeTimeout.Token).ConfigureAwait(false);
            }

            handshakeStep = "DirectConnectCode";
            using (var codeFrame = PacketCodec.Encode(
                       PacketKind.Control,
                       MasterControlPacketIds.DirectConnectCode,
                       MasterControlProtocol.SchemaVersion,
                       new DirectConnectCode(directConnectCode.Code),
                       DirectConnectCode.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, codeFrame, handshakeTimeout.Token).ConfigureAwait(false);
            }

            handshakeStep = "NodeAccepted";
            using var acceptedFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAccepted,
                handshakeTimeout.Token).ConfigureAwait(false);
            var accepted = PacketCodec.Decode(acceptedFrame, NodeAccepted.Codec);
            if (!string.Equals(accepted.NodeId, hello.NodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backend accepted a different Gateway node id than requested.");
            }

            return accepted;
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested && handshakeTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out during {handshakeStep} after {Math.Max(1, m_Options.HandshakeTimeoutMilliseconds)} ms.",
                e);
        }
    }

    private async Task DrainBackendFramesAsync(
        BackendPeer peer,
        BackendSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await PacketFrameReader.ReadAsync(
                    session.Stream,
                    MasterControlProtocol.TrustedControlPlanePolicy,
                    cancellationToken).ConfigureAwait(false);

                if (frame == null)
                {
                    m_PeerStates[peer.Node.MasterConnectionId] = "Disconnected";
                    return;
                }

                using (frame)
                {
                    if (frame.Header.PacketId == Pid.GATE_BACKEND_ROUTE)
                    {
                        await HandleBackendRouteFrameAsync(peer, frame, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            m_PeerStates[peer.Node.MasterConnectionId] = "Disconnected";
            logger.LogWarning(
                e,
                "Gateway Backend direct session ended. BackendKind={BackendKind}, BackendNodeId={NodeId}, Endpoint={Address}:{Port}.",
                peer.Node.BackendKind,
                peer.Node.NodeId,
                peer.Node.GatewayEndpoint.IPAddress,
                peer.Node.GatewayEndpoint.Port);
        }
        finally
        {
            if (ReferenceEquals(peer.CurrentSession, session))
            {
                peer.CurrentSession = null;
                m_PeerDirectConnectionIds.TryRemove(peer.Node.MasterConnectionId, out _);
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask HandleBackendRouteFrameAsync(
        BackendPeer peer,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
        {
            logger.LogWarning(
                "Backend route frame used unsupported version. BackendKind={BackendKind}, BackendNodeId={NodeId}, Version={Version}.",
                peer.Node.BackendKind,
                peer.Node.NodeId,
                frame.Header.Version);
            return;
        }

        var envelope = PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec);
        if (!string.Equals(envelope.BackendKind, peer.Node.BackendKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backend route frame used a different Backend kind than the trusted session.");
        }

        var received = new BackendRouteFrameReceived(
            peer.Node.BackendKind,
            peer.Node.NodeId,
            peer.Node.MasterConnectionId,
            envelope);
        var handlers = RouteFrameReceived;
        if (handlers == null)
        {
            return;
        }

        foreach (BackendRouteFrameReceivedHandler handler in handlers.GetInvocationList())
        {
            await handler(received, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<PacketFrame> ReadRequiredHandshakeFrameAsync(
        Stream stream,
        ushort expectedPacketId,
        CancellationToken cancellationToken)
    {
        var frame = await PacketFrameReader.ReadAsync(
            stream,
            MasterControlProtocol.UntrustedHandshakePolicy,
            cancellationToken).ConfigureAwait(false);

        if (frame == null)
        {
            throw new EndOfStreamException("Backend connection closed before Gateway handshake completed.");
        }

        try
        {
            MasterControlProtocol.ValidateControlFrame(frame, expectedPacketId);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_GatewayIdentity.NodeId))
        {
            throw new InvalidOperationException("MasterConnection:NodeId must be configured before connecting to Backend nodes.");
        }
    }

    private static bool HasSameEndpoint(BackendNodeEndpoint left, BackendNodeEndpoint right)
    {
        return string.Equals(left.GatewayEndpoint.IPAddress, right.GatewayEndpoint.IPAddress, StringComparison.Ordinal) &&
               left.GatewayEndpoint.Port == right.GatewayEndpoint.Port &&
               left.GatewayEndpoint.UseTls == right.GatewayEndpoint.UseTls;
    }

    private static string NormalizeBackendKind(string backendKind)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        return backendKind.Trim();
    }

    private static async Task WaitForShutdownAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class BackendPeer(
        BackendNodeEndpoint node,
        CancellationTokenSource cancellation)
    {
        public BackendNodeEndpoint Node { get; } = node;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public SemaphoreSlim ConnectLock { get; } = new(1, 1);

        public BackendSession? CurrentSession { get; set; }

        public Task? DrainTask { get; set; }

        public async ValueTask DisposeSessionAsync()
        {
            var session = CurrentSession;
            CurrentSession = null;

            if (session != null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class BackendSession(
        string backendKind,
        string nodeId,
        string masterConnectionId,
        string directConnectionId,
        Stream stream,
        Socket socket) : IBackendRouteSession, IAsyncDisposable
    {
        private readonly SemaphoreSlim m_WriteLock = new(1, 1);
        private int m_Disposed;

        public string BackendKind { get; } = backendKind;

        public string NodeId { get; } = nodeId;

        public string MasterConnectionId { get; } = masterConnectionId;

        public string DirectConnectionId { get; } = directConnectionId;

        public Stream Stream { get; } = stream;

        public bool IsConnected => Volatile.Read(ref m_Disposed) == 0;

        public async ValueTask WriteAsync(PacketFrame frame, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new ObjectDisposedException(nameof(BackendSession));
            }

            await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PacketFrameWriter.WriteAsync(Stream, frame, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_WriteLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_Disposed, 1, 0) != 0)
            {
                return;
            }

            try
            {
                await Stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                socket.Dispose();
                m_WriteLock.Dispose();
            }
        }
    }
}
