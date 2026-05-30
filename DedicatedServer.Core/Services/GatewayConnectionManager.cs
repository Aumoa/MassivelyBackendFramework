using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DedicatedServer.Options;
using DedicatedServer.Runtime;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace DedicatedServer.Services;

internal interface IGatewayConnectionStatusProvider
{
    ServiceAdminStatusItem[] GetStatusItems();
}

internal sealed class GatewayConnectionManager(
    IOptions<GatewayListenerOptions> options,
    IDedicatedWorldRuntime worldRuntime,
    ILogger<GatewayConnectionManager> logger) : IHostedService, IGatewayConnectionStatusProvider
{
    private readonly GatewayListenerOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly ConcurrentDictionary<Guid, Task> m_ConnectionTasks = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayConnectionStates = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayNodeIds = [];
    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private X509Certificate2? m_Cert;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (m_Options.UseTls)
        {
            m_Cert = await LoadCertificateAsync(m_Options, cancellationToken).ConfigureAwait(false);
        }

        var listenAddress = IPAddress.Parse(m_Options.IPAddress);
        m_Socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (listenAddress.Equals(IPAddress.IPv6Any))
        {
            m_Socket.DualMode = true;
        }

        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        m_Socket.Bind(new IPEndPoint(listenAddress, m_Options.Port));
        m_Socket.Listen(m_Options.Backlog);

        m_AcceptTask = AcceptLoopAsync(m_Shutdown.Token);
        logger.LogInformation("Dedicated Gateway listener is running on {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await m_Shutdown.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();

        if (m_AcceptTask != null)
        {
            await WaitForShutdownAsync(m_AcceptTask, cancellationToken).ConfigureAwait(false);
        }

        var connectionTasks = m_ConnectionTasks.Values.ToArray();
        if (connectionTasks.Length > 0)
        {
            await WaitForShutdownAsync(Task.WhenAll(connectionTasks), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;

            try
            {
                socket = await m_Socket!.AcceptAsync(cancellationToken).ConfigureAwait(false);
                socket.NoDelay = true;
                StartConnection(socket, cancellationToken);
                socket = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket?.Dispose();
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                socket?.Dispose();
                return;
            }
            catch (Exception e)
            {
                socket?.Dispose();
                logger.LogError(e, "Error occurred while accepting a Gateway connection.");
            }
        }
    }

    private void StartConnection(Socket socket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid();
        var task = HandleConnectionAsync(connectionId, socket, cancellationToken);
        m_ConnectionTasks.TryAdd(connectionId, task);

        _ = task.ContinueWith(
            completed =>
            {
                m_ConnectionTasks.TryRemove(connectionId, out _);
                m_GatewayConnectionStates.TryRemove(connectionId, out _);
                m_GatewayNodeIds.TryRemove(connectionId, out _);
                socket.Dispose();

                if (completed.Exception != null)
                {
                    logger.LogError(completed.Exception, "Unhandled Dedicated Gateway connection task failure.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(Guid connectionId, Socket socket, CancellationToken cancellationToken)
    {
        await using var networkStream = new NetworkStream(socket, ownsSocket: false);
        SslStream? sslStream = null;
        Stream activeStream = networkStream;
        string gatewayNodeId = string.Empty;
        m_GatewayConnectionStates[connectionId] = "Handshaking";

        try
        {
            if (m_Cert != null)
            {
                sslStream = new SslStream(networkStream, leaveInnerStreamOpen: true);
                await sslStream.AuthenticateAsServerAsync(
                    m_Cert,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls13,
                    checkCertificateRevocation: true).ConfigureAwait(false);
                activeStream = sslStream;
            }

            var acceptedGateway = await AuthenticateGatewayAsync(connectionId, activeStream, cancellationToken).ConfigureAwait(false);
            gatewayNodeId = acceptedGateway.NodeId;
            m_GatewayNodeIds[connectionId] = gatewayNodeId;
            m_GatewayConnectionStates[connectionId] = "Trusted";
            logger.LogInformation(
                "Dedicated accepted Gateway direct connection. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);

            await DrainGatewayFramesAsync(connectionId, gatewayNodeId, activeStream, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Gateway direct connection closed. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e) when (IsRemoteDisconnect(e))
        {
            m_GatewayConnectionStates[connectionId] = "Disconnected";
            logger.LogInformation(
                "Gateway direct connection was closed by the remote peer. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);
            logger.LogDebug(e, "Gateway direct remote disconnect details.");
        }
        catch (Exception e)
        {
            m_GatewayConnectionStates[connectionId] = "Error";
            logger.LogWarning(
                e,
                "Gateway direct connection ended with an unexpected error. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);
        }
        finally
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<NodeAccepted> AuthenticateGatewayAsync(Guid connectionId, Stream stream, CancellationToken cancellationToken)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, m_Options.HandshakeTimeoutMilliseconds)));

        var challenge = CreateChallenge();
        using (var challengeFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   MasterControlPacketIds.NodeAuthChallenge,
                   MasterControlProtocol.SchemaVersion,
                   challenge,
                   NodeAuthChallenge.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, challengeFrame, handshakeTimeout.Token).ConfigureAwait(false);
        }

        using var helloFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.NodeHello,
            handshakeTimeout.Token).ConfigureAwait(false);
        var hello = PacketCodec.Decode(helloFrame, NodeHello.Codec);
        if (hello.NodeKind != MasterNodeKind.Gateway)
        {
            throw new UnauthorizedAccessException($"Dedicated only accepts Gateway nodes, but received {hello.NodeKind}.");
        }

        if (hello.ProtocolVersion != MasterControlProtocol.SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported Gateway protocol version {hello.ProtocolVersion}.");
        }

        using var proofFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.NodeAuthProof,
            handshakeTimeout.Token).ConfigureAwait(false);
        var proof = PacketCodec.Decode(proofFrame, NodeAuthProof.Codec);
        if (!MasterNodeAuthenticator.VerifyProof(challenge, hello, proof, m_Options.SharedSecret))
        {
            throw new UnauthorizedAccessException($"Gateway authentication proof was rejected for node '{hello.NodeId}'.");
        }

        var accepted = new NodeAccepted(hello.NodeId, connectionId.ToString("N"));
        using (var acceptedFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   MasterControlPacketIds.NodeAccepted,
                   MasterControlProtocol.SchemaVersion,
                   accepted,
                   NodeAccepted.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, acceptedFrame, handshakeTimeout.Token).ConfigureAwait(false);
        }

        return accepted;
    }

    private async Task DrainGatewayFramesAsync(
        Guid connectionId,
        string gatewayNodeId,
        Stream stream,
        CancellationToken cancellationToken)
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
                var context = new DedicatedGatewayPacketContext(
                    gatewayNodeId,
                    connectionId,
                    frame.Header.Kind,
                    frame.Header.PacketId,
                    frame.Header.Version,
                    DateTimeOffset.UtcNow);
                await worldRuntime.HandleGatewayPacketAsync(context, frame.Payload, cancellationToken).ConfigureAwait(false);
            }
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
            throw new EndOfStreamException("Gateway connection closed before Dedicated handshake completed.");
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

    private static NodeAuthChallenge CreateChallenge()
    {
        byte[] nonce = new byte[MasterControlProtocol.AuthNonceLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(nonce);
        return new NodeAuthChallenge(Guid.NewGuid().ToString("N"), nonce);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_Options.SharedSecret))
        {
            throw new InvalidOperationException("GatewayListener:SharedSecret must be configured before accepting Gateway connections.");
        }
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        var items = new List<ServiceAdminStatusItem>
        {
            new("Gateway", "Listener", $"{m_Options.IPAddress}:{m_Options.Port}"),
            new("Gateway", "TLS", m_Options.UseTls ? "Enabled" : "Disabled"),
            new("Gateway", "Active connections", m_GatewayConnectionStates.Count.ToString())
        };

        foreach (var pair in m_GatewayConnectionStates.OrderBy(static pair => pair.Key))
        {
            var group = $"Gateway {pair.Key:N}"[..24];
            var nodeId = m_GatewayNodeIds.TryGetValue(pair.Key, out var value) ? value : "unknown";
            items.Add(new ServiceAdminStatusItem(group, "State", pair.Value));
            items.Add(new ServiceAdminStatusItem(group, "Node", nodeId));
            items.Add(new ServiceAdminStatusItem(group, "Connection", pair.Key.ToString("N")));
        }

        return [.. items];
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is EndOfStreamException ||
               exception is IOException { InnerException: SocketException innerSocketException } && IsRemoteDisconnect(innerSocketException) ||
               exception is SocketException socketException && IsRemoteDisconnect(socketException);
    }

    private static bool IsRemoteDisconnect(SocketException exception)
    {
        return exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown;
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

    private static async Task<X509Certificate2> LoadCertificateAsync(GatewayListenerOptions options, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certs = store.Certificates.Find(X509FindType.FindBySubjectName, options.CertificateSubjectName, validOnly: false);
            if (certs.Count == 0)
            {
                throw new InvalidOperationException($"No certificate found for subject '{options.CertificateSubjectName}'.");
            }

            return certs[0];
        }, cancellationToken).ConfigureAwait(false);
    }
}
