using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using GatewayServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal interface IDedicatedConnectionStatusProvider
{
    ServiceAdminStatusItem[] GetStatusItems();
}

internal interface IGatewayMasterConnectionIdentitySink
{
    void SetMasterConnectionId(string? masterConnectionId);
}

internal sealed class DedicatedConnectionManager(
    IOptions<DedicatedConnectionOptions> options,
    IOptions<MasterConnectionOptions> gatewayIdentity,
    IDedicatedNodeCatalog catalog,
    IDirectConnectCodeIssuer directConnectCodeIssuer,
    ILogger<DedicatedConnectionManager> logger) : IHostedService, IDedicatedConnectionStatusProvider, IGatewayMasterConnectionIdentitySink
{
    private readonly DedicatedConnectionOptions m_Options = options.Value;
    private readonly MasterConnectionOptions m_GatewayIdentity = gatewayIdentity.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_PeersSync = new();
    private readonly Dictionary<string, DedicatedPeer> m_Peers = [];
    private readonly ConcurrentDictionary<string, string> m_PeerStates = [];
    private readonly ConcurrentDictionary<string, string> m_PeerDirectConnectionIds = [];
    private string? m_MasterConnectionId;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            logger.LogInformation("Gateway Dedicated direct connections are disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        catalog.SnapshotChanged += OnDedicatedSnapshotChanged;
        ApplySnapshot(catalog.GetSnapshot());
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        catalog.SnapshotChanged -= OnDedicatedSnapshotChanged;
        await m_Shutdown.CancelAsync().ConfigureAwait(false);

        DedicatedPeer[] peers;
        lock (m_PeersSync)
        {
            peers = [.. m_Peers.Values];
            m_Peers.Clear();
        }

        foreach (var peer in peers)
        {
            await peer.Cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (peers.Length > 0)
        {
            await WaitForShutdownAsync(Task.WhenAll(peers.Select(static peer => peer.Task)), cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnDedicatedSnapshotChanged(DedicatedNodeSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(DedicatedNodeSnapshot snapshot)
    {
        var desired = snapshot.Nodes
            .GroupBy(static node => node.MasterConnectionId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToDictionary(static node => node.MasterConnectionId, StringComparer.Ordinal);

        List<DedicatedPeer> removed = [];

        lock (m_PeersSync)
        {
            foreach (var current in m_Peers.Values.ToArray())
            {
                if (!desired.TryGetValue(current.Node.MasterConnectionId, out var next) ||
                    !HasSameEndpoint(current.Node, next))
                {
                    m_Peers.Remove(current.Node.MasterConnectionId);
                    m_PeerStates.TryRemove(current.Node.MasterConnectionId, out _);
                    removed.Add(current);
                }
            }

            foreach (var node in desired.Values)
            {
                if (m_Peers.ContainsKey(node.MasterConnectionId))
                {
                    continue;
                }

                var peerCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_Shutdown.Token);
                var task = RunPeerAsync(node, peerCancellation.Token);
                m_Peers[node.MasterConnectionId] = new DedicatedPeer(node, peerCancellation, task);
                m_PeerStates[node.MasterConnectionId] = "Discovered";
            }
        }

        foreach (var peer in removed)
        {
            _ = peer.Cancellation.CancelAsync();
        }
    }

    private async Task RunPeerAsync(DedicatedNodeEndpoint node, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                m_PeerStates[node.MasterConnectionId] = "Connecting";
                await RunSessionAsync(node, cancellationToken).ConfigureAwait(false);
                m_PeerStates[node.MasterConnectionId] = "Disconnected";
                logger.LogInformation(
                    "Dedicated direct connection closed. DedicatedNodeId={NodeId}, Endpoint={Address}:{Port}.",
                    node.NodeId,
                    node.GatewayEndpoint.IPAddress,
                    node.GatewayEndpoint.Port);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException e)
            {
                m_PeerStates[node.MasterConnectionId] = "Handshake timeout";
                logger.LogWarning(
                    "Dedicated direct handshake timed out. DedicatedNodeId={NodeId}, Message={Message}.",
                    node.NodeId,
                    e.Message);
            }
            catch (Exception e)
            {
                m_PeerStates[node.MasterConnectionId] = "Reconnecting";
                logger.LogWarning(
                    e,
                    "Dedicated direct connection failed. DedicatedNodeId={NodeId}, Endpoint={Address}:{Port}.",
                    node.NodeId,
                    node.GatewayEndpoint.IPAddress,
                    node.GatewayEndpoint.Port);
            }
            finally
            {
                m_PeerDirectConnectionIds.TryRemove(node.MasterConnectionId, out _);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Max(1, m_Options.ReconnectDelayMilliseconds)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunSessionAsync(DedicatedNodeEndpoint node, CancellationToken cancellationToken)
    {
        var endpoint = node.GatewayEndpoint;
        using var socket = await MasterEndpointResolver.ConnectTcpAsync(
            endpoint.IPAddress,
            endpoint.Port,
            cancellationToken).ConfigureAwait(false);
        m_PeerStates[node.MasterConnectionId] = "Connected";
        logger.LogInformation(
            "Gateway connected to Dedicated node. DedicatedNodeId={NodeId}, Endpoint={Address}:{Port}.",
            node.NodeId,
            endpoint.IPAddress,
            endpoint.Port);

        await using var networkStream = new NetworkStream(socket, ownsSocket: false);
        SslStream? sslStream = null;
        Stream activeStream = networkStream;

        try
        {
            if (endpoint.UseTls)
            {
                sslStream = new SslStream(networkStream, leaveInnerStreamOpen: true);
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
                "Gateway Dedicated direct session trusted. DedicatedNodeId={DedicatedNodeId}, GatewayNodeId={GatewayNodeId}, DedicatedConnectionId={ConnectionId}.",
                node.NodeId,
                accepted.NodeId,
                accepted.ConnectionId);

            await DrainDedicatedFramesAsync(activeStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<NodeAccepted> CompleteHandshakeAsync(
        Stream stream,
        DedicatedNodeEndpoint node,
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
            handshakeStep = "DirectConnectCodeRequest";
            var directConnectCode = await directConnectCodeIssuer
                .RequestDirectConnectCodeAsync(
                    MasterNodeKind.Dedicated,
                    node.MasterConnectionId,
                    handshakeTimeout.Token)
                .ConfigureAwait(false);

            var hello = new NodeHello(
                MasterNodeKind.Gateway,
                m_GatewayIdentity.NodeId,
                m_GatewayIdentity.DisplayName,
                MasterControlProtocol.SchemaVersion,
                m_MasterConnectionId ?? string.Empty);

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
                throw new InvalidOperationException("Dedicated accepted a different Gateway node id than requested.");
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
            throw new EndOfStreamException("Dedicated connection closed before Gateway handshake completed.");
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

    private static async Task DrainDedicatedFramesAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await PacketFrameReader.ReadAsync(
                stream,
                MasterControlProtocol.TrustedControlPlanePolicy,
                cancellationToken).ConfigureAwait(false);

            if (frame == null)
            {
                return;
            }

            using (frame)
            {
            }
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_GatewayIdentity.NodeId))
        {
            throw new InvalidOperationException("MasterConnection:NodeId must be configured before connecting to Dedicated nodes.");
        }

    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        DedicatedPeer[] peers;
        lock (m_PeersSync)
        {
            peers = [.. m_Peers.Values];
        }

        var items = new List<ServiceAdminStatusItem>
        {
            new("Dedicated", "Configured", m_Options.Enabled ? "Enabled" : "Disabled"),
            new("Dedicated", "Discovered nodes", peers.Length.ToString())
        };

        foreach (var peer in peers.OrderBy(static peer => peer.Node.NodeId, StringComparer.Ordinal))
        {
            var state = m_PeerStates.TryGetValue(peer.Node.MasterConnectionId, out var peerState)
                ? peerState
                : "Unknown";
            var group = $"Dedicated {peer.Node.NodeId}";
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

    private static bool HasSameEndpoint(DedicatedNodeEndpoint left, DedicatedNodeEndpoint right)
    {
        return string.Equals(left.GatewayEndpoint.IPAddress, right.GatewayEndpoint.IPAddress, StringComparison.Ordinal) &&
               left.GatewayEndpoint.Port == right.GatewayEndpoint.Port &&
               left.GatewayEndpoint.UseTls == right.GatewayEndpoint.UseTls;
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

    private sealed record DedicatedPeer(
        DedicatedNodeEndpoint Node,
        CancellationTokenSource Cancellation,
        Task Task);
}
