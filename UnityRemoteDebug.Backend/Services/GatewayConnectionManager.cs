using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;
using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Backend.Options;

namespace UnityRemoteDebug.Backend.Services;

internal sealed class GatewayConnectionManager(
    IOptions<GatewayListenerOptions> options,
    IOptions<MasterConnectionOptions> backendIdentity,
    IOptions<BackendRegistrationOptions> backendRegistration,
    IDirectConnectCodeValidator directConnectCodeValidator,
    RemoteDebugClientRegistry remoteDebugClients,
    ILogger<GatewayConnectionManager> logger) : IHostedService
{
    private readonly GatewayListenerOptions m_Options = options.Value;
    private readonly MasterConnectionOptions m_BackendIdentity = backendIdentity.Value;
    private readonly BackendRegistrationOptions m_BackendRegistration = backendRegistration.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly ConcurrentDictionary<Guid, Task> m_ConnectionTasks = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayConnectionStates = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayNodeIds = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayDisplayNames = [];
    private readonly ConcurrentDictionary<Guid, string> m_GatewayMasterConnectionIds = [];
    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private X509Certificate2? m_Cert;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (m_Options.UseTls)
        {
            m_Cert = await LoadCertificateAsync(m_Options, cancellationToken).ConfigureAwait(false);
        }

        var listenAddress = await ResolveBindAddressAsync(m_Options.IPAddress, cancellationToken).ConfigureAwait(false);
        m_Socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (listenAddress.Equals(IPAddress.IPv6Any))
        {
            m_Socket.DualMode = true;
        }

        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        m_Socket.Bind(new IPEndPoint(listenAddress, m_Options.Port));
        m_Socket.Listen(m_Options.Backlog);

        logger.LogInformation(
            "Unity RemoteDebug Backend Gateway listener is running on {Address}:{Port}.",
            m_Options.IPAddress,
            m_Options.Port);
        m_AcceptTask = AcceptLoopAsync(m_Shutdown.Token);
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
                logger.LogError(e, "Error occurred while accepting a Gateway connection for Unity RemoteDebug Backend.");
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
                m_GatewayDisplayNames.TryRemove(connectionId, out _);
                m_GatewayMasterConnectionIds.TryRemove(connectionId, out _);
                socket.Dispose();

                if (completed.Exception != null)
                {
                    logger.LogError(completed.Exception, "Unhandled Unity RemoteDebug Backend Gateway connection task failure.");
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
            m_GatewayConnectionStates[connectionId] = "Trusted";
            logger.LogInformation(
                "Unity RemoteDebug Backend accepted Gateway direct connection. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);

            await ProcessGatewayFramesAsync(connectionId, activeStream, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Unity RemoteDebug Backend Gateway direct connection closed. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
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
                "Unity RemoteDebug Backend Gateway connection was closed by the remote peer. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connectionId,
                gatewayNodeId,
                socket.RemoteEndPoint);
            logger.LogDebug(e, "Unity RemoteDebug Backend Gateway remote disconnect details.");
        }
        catch (Exception e)
        {
            m_GatewayConnectionStates[connectionId] = "Error";
            logger.LogWarning(
                e,
                "Unity RemoteDebug Backend Gateway connection ended with an unexpected error. ConnectionId={ConnectionId}, GatewayNodeId={GatewayNodeId}, RemoteEndPoint={RemoteEndPoint}.",
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
            throw new UnauthorizedAccessException($"Unity RemoteDebug Backend only accepts Gateway nodes, but received {hello.NodeKind}.");
        }

        if (hello.ProtocolVersion != MasterControlProtocol.SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported Gateway protocol version {hello.ProtocolVersion}.");
        }

        if (string.IsNullOrWhiteSpace(hello.MasterConnectionId))
        {
            throw new UnauthorizedAccessException("Gateway Master connection id is required for Unity RemoteDebug Backend direct connection approval.");
        }

        m_GatewayNodeIds[connectionId] = hello.NodeId;
        m_GatewayDisplayNames[connectionId] = hello.DisplayName;
        m_GatewayMasterConnectionIds[connectionId] = hello.MasterConnectionId;

        using var codeFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.DirectConnectCode,
            handshakeTimeout.Token).ConfigureAwait(false);
        var directConnectCode = PacketCodec.Decode(codeFrame, DirectConnectCode.Codec);
        var validation = await directConnectCodeValidator.ValidateDirectConnectCodeAsync(
            directConnectCode.Code,
            hello.NodeId,
            hello.MasterConnectionId,
            handshakeTimeout.Token).ConfigureAwait(false);
        if (!string.Equals(validation.GatewayNodeId, hello.NodeId, StringComparison.Ordinal) ||
            !string.Equals(validation.GatewayMasterConnectionId, hello.MasterConnectionId, StringComparison.Ordinal) ||
            validation.TargetNodeKind != MasterNodeKind.Backend ||
            !string.Equals(validation.TargetNodeId, m_BackendIdentity.NodeId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Direct connect code validation returned an unexpected Unity RemoteDebug Backend connection identity.");
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

    private async Task ProcessGatewayFramesAsync(
        Guid connectionId,
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
                if (frame.Header.PacketId != Pid.GATE_BACKEND_ROUTE)
                {
                    logger.LogDebug(
                        "Unity RemoteDebug Backend ignored Gateway frame. ConnectionId={ConnectionId}, PacketKind={PacketKind}, PacketId={PacketId}, Version={Version}.",
                        connectionId,
                        frame.Header.Kind,
                        frame.Header.PacketId,
                        frame.Header.Version);
                    continue;
                }

                await HandleGatewayBackendRouteFrameAsync(connectionId, stream, frame, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleGatewayBackendRouteFrameAsync(
        Guid connectionId,
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
        {
            logger.LogWarning(
                "Unity RemoteDebug Backend ignored Gateway route frame with unsupported version. ConnectionId={ConnectionId}, Version={Version}.",
                connectionId,
                frame.Header.Version);
            return;
        }

        var envelope = PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec);
        if (envelope.RoutedKind == PacketKind.Response)
        {
            logger.LogWarning(
                "Unity RemoteDebug Backend received an unexpected Gateway routed response. ConnectionId={ConnectionId}, RouteId={RouteId}.",
                connectionId,
                envelope.RouteId);
            return;
        }

        if (!string.Equals(envelope.BackendKind, m_BackendRegistration.BackendKind, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Unity RemoteDebug Backend received a route for another Backend kind. ConnectionId={ConnectionId}, ExpectedBackendKind={ExpectedBackendKind}, ReceivedBackendKind={ReceivedBackendKind}, RouteId={RouteId}.",
                connectionId,
                m_BackendRegistration.BackendKind,
                envelope.BackendKind,
                envelope.RouteId);

            if (envelope.RoutedKind == PacketKind.Request)
            {
                await WriteErrorResponseAsync(
                    stream,
                    envelope,
                    $"Backend kind '{envelope.BackendKind}' is not handled by this Unity RemoteDebug Backend.",
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (envelope.RoutedKind == PacketKind.Notify)
        {
            HandleBackendRouteNotify(connectionId, envelope);
            return;
        }

        try
        {
            await HandleBackendRouteRequestAsync(connectionId, stream, envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Unity RemoteDebug Backend failed to handle routed request. ConnectionId={ConnectionId}, RouteId={RouteId}, PacketId={PacketId}.",
                connectionId,
                envelope.RouteId,
                envelope.RoutedPacketId);
            await WriteErrorResponseAsync(stream, envelope, "Unity RemoteDebug Backend request failed.", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleBackendRouteRequestAsync(
        Guid connectionId,
        Stream stream,
        GatewayBackendRouteEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.RoutedVersion != RemoteDebugProtocol.SchemaVersion)
        {
            await WriteErrorResponseAsync(
                stream,
                envelope,
                $"Unsupported Unity RemoteDebug Backend protocol version {envelope.RoutedVersion}.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using var routedFrame = envelope.CreateRoutedFrame();
        var now = DateTimeOffset.UtcNow;
        switch (envelope.RoutedPacketId)
        {
            case RemoteDebugPacketIds.BackendStatusRequest:
                PacketCodec.Decode(routedFrame, RemoteDebugBackendStatusRequest.Codec);
                await WriteRoutedResponseAsync(
                    stream,
                    envelope.RouteId,
                    RemoteDebugPacketIds.BackendStatusResponse,
                    new RemoteDebugBackendStatusResponse(
                        m_BackendRegistration.BackendKind,
                        GetTrustedGatewayConnectionCount(),
                        remoteDebugClients.GetClientCount(now),
                        now.ToUnixTimeMilliseconds()),
                    RemoteDebugBackendStatusResponse.Codec,
                    cancellationToken).ConfigureAwait(false);
                break;

            case RemoteDebugPacketIds.BackendClientListRequest:
                PacketCodec.Decode(routedFrame, RemoteDebugBackendClientListRequest.Codec);
                await WriteRoutedResponseAsync(
                    stream,
                    envelope.RouteId,
                    RemoteDebugPacketIds.BackendClientListResponse,
                    new RemoteDebugBackendClientListResponse(
                        remoteDebugClients.GetClientSnapshots(now),
                        now.ToUnixTimeMilliseconds()),
                    RemoteDebugBackendClientListResponse.Codec,
                    cancellationToken).ConfigureAwait(false);
                break;

            case RemoteDebugPacketIds.BackendClientRegisterRequest:
                var registerRequest = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientRegisterRequest.Codec);
                await WriteRoutedResponseAsync(
                    stream,
                    envelope.RouteId,
                    RemoteDebugPacketIds.BackendClientRegisterResponse,
                    remoteDebugClients.Register(registerRequest, now),
                    RemoteDebugBackendClientRegisterResponse.Codec,
                    cancellationToken).ConfigureAwait(false);
                break;

            case RemoteDebugPacketIds.BackendClientHeartbeatRequest:
                var heartbeatRequest = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientHeartbeatRequest.Codec);
                if (!remoteDebugClients.TryHeartbeat(heartbeatRequest, now, out var heartbeatResponse) ||
                    heartbeatResponse == null)
                {
                    await WriteErrorResponseAsync(
                        stream,
                        envelope,
                        "RemoteDebug client session is not registered.",
                        cancellationToken).ConfigureAwait(false);
                    break;
                }

                await WriteRoutedResponseAsync(
                    stream,
                    envelope.RouteId,
                    RemoteDebugPacketIds.BackendClientHeartbeatResponse,
                    heartbeatResponse,
                    RemoteDebugBackendClientHeartbeatResponse.Codec,
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                logger.LogWarning(
                    "Unity RemoteDebug Backend rejected unsupported routed request. ConnectionId={ConnectionId}, RouteId={RouteId}, PacketId={PacketId}.",
                    connectionId,
                    envelope.RouteId,
                    envelope.RoutedPacketId);
                await WriteErrorResponseAsync(
                    stream,
                    envelope,
                    $"Unsupported Unity RemoteDebug Backend packet id {envelope.RoutedPacketId}.",
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private void HandleBackendRouteNotify(
        Guid connectionId,
        GatewayBackendRouteEnvelope envelope)
    {
        if (envelope.RoutedVersion != RemoteDebugProtocol.SchemaVersion)
        {
            logger.LogWarning(
                "Unity RemoteDebug Backend ignored routed notify with unsupported protocol version. ConnectionId={ConnectionId}, RouteId={RouteId}, Version={Version}.",
                connectionId,
                envelope.RouteId,
                envelope.RoutedVersion);
            return;
        }

        using var routedFrame = envelope.CreateRoutedFrame();
        switch (envelope.RoutedPacketId)
        {
            case RemoteDebugPacketIds.BackendClientDisconnectNotify:
                var notify = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientDisconnectNotify.Codec);
                if (!remoteDebugClients.Disconnect(notify))
                {
                    logger.LogDebug(
                        "Unity RemoteDebug Backend ignored disconnect notify for an unknown client session. ConnectionId={ConnectionId}, ClientId={ClientId}.",
                        connectionId,
                        notify.ClientId);
                }

                break;

            default:
                logger.LogDebug(
                    "Unity RemoteDebug Backend ignored Gateway routed notify. ConnectionId={ConnectionId}, RouteId={RouteId}, PacketId={PacketId}.",
                    connectionId,
                    envelope.RouteId,
                    envelope.RoutedPacketId);
                break;
        }
    }

    private async Task WriteErrorResponseAsync(
        Stream stream,
        GatewayBackendRouteEnvelope request,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await WriteRoutedResponseAsync(
            stream,
            request.RouteId,
            RemoteDebugPacketIds.BackendErrorResponse,
            new RemoteDebugBackendErrorResponse(
                request.RoutedPacketId,
                request.RoutedVersion,
                errorMessage),
            RemoteDebugBackendErrorResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteRoutedResponseAsync<TPacket>(
        Stream stream,
        Guid routeId,
        ushort routedPacketId,
        TPacket value,
        IPacketCodec<TPacket> codec,
        CancellationToken cancellationToken)
    {
        using var routedFrame = PacketCodec.Encode(
            PacketKind.Response,
            routedPacketId,
            RemoteDebugProtocol.SchemaVersion,
            value,
            codec);
        var responseEnvelope = new GatewayBackendRouteEnvelope(
            m_BackendRegistration.BackendKind,
            routeId,
            PacketKind.Response,
            routedPacketId,
            RemoteDebugProtocol.SchemaVersion,
            routedFrame.Payload.ToArray());
        using var gatewayFrame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            responseEnvelope,
            GatewayBackendRouteEnvelope.Codec);
        await PacketFrameWriter.WriteAsync(stream, gatewayFrame, cancellationToken).ConfigureAwait(false);
    }

    private int GetTrustedGatewayConnectionCount()
    {
        return m_GatewayConnectionStates.Count(static pair => string.Equals(pair.Value, "Trusted", StringComparison.Ordinal));
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
            throw new EndOfStreamException("Gateway connection closed before Unity RemoteDebug Backend handshake completed.");
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

    private static async Task<IPAddress> ResolveBindAddressAsync(string address, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            return ipAddress;
        }

        var addresses = await Dns.GetHostAddressesAsync(address, cancellationToken).ConfigureAwait(false);
        return addresses.FirstOrDefault() ?? throw new InvalidOperationException($"Could not resolve listen address '{address}'.");
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
}
