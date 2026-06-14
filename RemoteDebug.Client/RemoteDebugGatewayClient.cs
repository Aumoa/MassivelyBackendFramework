using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GatewayServer.Protocols;
using PacketCore;
using RemoteDebugServer.Protocols;

namespace RemoteDebug;

public sealed class RemoteDebugGatewayClient : IDisposable, IAsyncDisposable
{
    private readonly TcpClient m_TcpClient;
    private readonly Stream m_Stream;
    private readonly string m_BackendKind;
    private readonly int m_RequestTimeoutMilliseconds;
    private readonly SemaphoreSlim m_RouteLock = new SemaphoreSlim(1, 1);
    private int m_Disposed;

    private RemoteDebugGatewayClient(
        TcpClient tcpClient,
        Stream stream,
        string backendKind,
        int requestTimeoutMilliseconds,
        RemoteDebugSessionInfo session)
    {
        m_TcpClient = tcpClient;
        m_Stream = stream;
        m_BackendKind = backendKind;
        m_RequestTimeoutMilliseconds = requestTimeoutMilliseconds;
        Session = session;
    }

    public RemoteDebugSessionInfo Session { get; }

    public static async Task<RemoteDebugGatewayClient> ConnectAsync(
        RemoteDebugGatewayClientOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(options.RequestTimeoutMilliseconds));

        TcpClient? tcpClient = null;
        Stream? activeStream = null;

        try
        {
            tcpClient = new TcpClient
            {
                NoDelay = true
            };

            await WaitWithCancellationAsync(
                tcpClient.ConnectAsync(options.Host, options.Port),
                requestTimeout.Token).ConfigureAwait(false);

            var networkStream = tcpClient.GetStream();
            activeStream = networkStream;

            if (options.UseTls)
            {
                var sslStream = CreateSslStream(networkStream, options);
                activeStream = sslStream;
                await WaitWithCancellationAsync(
                    sslStream.AuthenticateAsClientAsync(
                        string.IsNullOrWhiteSpace(options.ServerName) ? options.Host : options.ServerName,
                        clientCertificates: null,
                        enabledSslProtocols: options.EnabledSslProtocols,
                        checkCertificateRevocation: options.CheckCertificateRevocation),
                    requestTimeout.Token).ConfigureAwait(false);
            }

            await ReadGatewayHandshakeAsync(activeStream, requestTimeout.Token).ConfigureAwait(false);

            var registerRequest = new RemoteDebugBackendClientRegisterRequest(
                options.ClientId,
                options.DisplayName,
                options.ClientVersion,
                options.UnityVersion,
                options.Capabilities);
            var registerResponse = await SendBackendRouteRequestCoreAsync(
                activeStream,
                options.BackendKind,
                RemoteDebugPacketIds.BackendClientRegisterRequest,
                RemoteDebugPacketIds.BackendClientRegisterResponse,
                registerRequest,
                RemoteDebugBackendClientRegisterRequest.Codec,
                RemoteDebugBackendClientRegisterResponse.Codec,
                requestTimeout.Token).ConfigureAwait(false);
            ValidateRegisterResponse(options, registerResponse);

            var session = new RemoteDebugSessionInfo(
                registerResponse.ClientId,
                registerResponse.SessionId,
                registerResponse.EnabledCapabilities,
                registerResponse.HeartbeatIntervalMilliseconds);
            var client = new RemoteDebugGatewayClient(
                tcpClient,
                activeStream,
                options.BackendKind.Trim(),
                options.RequestTimeoutMilliseconds,
                session);
            tcpClient = null;
            activeStream = null;
            return client;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && requestTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while connecting to RemoteDebug Backend through Gateway after {options.RequestTimeoutMilliseconds} ms.");
        }
        finally
        {
            activeStream?.Dispose();
            tcpClient?.Dispose();
        }
    }

    public async Task<RemoteDebugBackendClientHeartbeatResponse> HeartbeatAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var response = await SendRouteRequestAsync(
            RemoteDebugPacketIds.BackendClientHeartbeatRequest,
            RemoteDebugPacketIds.BackendClientHeartbeatResponse,
            new RemoteDebugBackendClientHeartbeatRequest(Session.ClientId, Session.SessionId),
            RemoteDebugBackendClientHeartbeatRequest.Codec,
            RemoteDebugBackendClientHeartbeatResponse.Codec,
            cancellationToken).ConfigureAwait(false);
        ValidateSessionResponse(response.ClientId, response.SessionId);
        return response;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return DisconnectAsync("Client disconnected.", cancellationToken);
    }

    public async Task DisconnectAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        ThrowIfDisposed();

        await SendRouteNotifyAsync(
            RemoteDebugPacketIds.BackendClientDisconnectNotify,
            new RemoteDebugBackendClientDisconnectNotify(Session.ClientId, Session.SessionId, reason),
            RemoteDebugBackendClientDisconnectNotify.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_RouteLock.Dispose();
        m_Stream.Dispose();
        m_TcpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_RouteLock.Dispose();
        await m_Stream.DisposeAsync().ConfigureAwait(false);
        m_TcpClient.Dispose();
    }

    private static SslStream CreateSslStream(NetworkStream networkStream, RemoteDebugGatewayClientOptions options)
    {
        return options.ServerCertificateValidationCallback == null
            ? new SslStream(networkStream, leaveInnerStreamOpen: false)
            : new SslStream(networkStream, leaveInnerStreamOpen: false, options.ServerCertificateValidationCallback);
    }

    private async Task<TResponse> SendRouteRequestAsync<TRequest, TResponse>(
        ushort requestPacketId,
        ushort responsePacketId,
        TRequest request,
        IPacketCodec<TRequest> requestCodec,
        IPacketCodec<TResponse> responseCodec,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(m_RequestTimeoutMilliseconds));

        var lockTaken = false;
        try
        {
            await m_RouteLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            lockTaken = true;
            return await SendBackendRouteRequestCoreAsync(
                m_Stream,
                m_BackendKind,
                requestPacketId,
                responsePacketId,
                request,
                requestCodec,
                responseCodec,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while routing RemoteDebug Backend packet {requestPacketId} through Gateway after {m_RequestTimeoutMilliseconds} ms.");
        }
        finally
        {
            if (lockTaken)
            {
                m_RouteLock.Release();
            }
        }
    }

    private async Task SendRouteNotifyAsync<TNotify>(
        ushort packetId,
        TNotify notify,
        IPacketCodec<TNotify> notifyCodec,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(m_RequestTimeoutMilliseconds));

        var lockTaken = false;
        try
        {
            await m_RouteLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            lockTaken = true;
            await SendBackendRouteNotifyCoreAsync(
                m_Stream,
                m_BackendKind,
                packetId,
                notify,
                notifyCodec,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while routing RemoteDebug Backend notify {packetId} through Gateway after {m_RequestTimeoutMilliseconds} ms.");
        }
        finally
        {
            if (lockTaken)
            {
                m_RouteLock.Release();
            }
        }
    }

    private static async Task<TResponse> SendBackendRouteRequestCoreAsync<TRequest, TResponse>(
        Stream stream,
        string backendKind,
        ushort requestPacketId,
        ushort responsePacketId,
        TRequest request,
        IPacketCodec<TRequest> requestCodec,
        IPacketCodec<TResponse> responseCodec,
        CancellationToken cancellationToken)
    {
        var routeId = Guid.NewGuid();
        using (var routedFrame = PacketCodec.Encode(
                   PacketKind.Request,
                   requestPacketId,
                   RemoteDebugProtocol.SchemaVersion,
                   request,
                   requestCodec))
        {
            var requestEnvelope = new GatewayBackendRouteEnvelope(
                backendKind,
                routeId,
                PacketKind.Request,
                requestPacketId,
                RemoteDebugProtocol.SchemaVersion,
                routedFrame.Payload.ToArray());
            using var gatewayFrame = PacketCodec.Encode(
                PacketKind.Request,
                Pid.GATE_BACKEND_ROUTE,
                GatewayBackendRouteEnvelope.ProtocolVersion,
                requestEnvelope,
                GatewayBackendRouteEnvelope.Codec);
            await PacketFrameWriter.WriteAsync(stream, gatewayFrame, cancellationToken).ConfigureAwait(false);
        }

        await ReadGatewayRouteAcceptedAsync(stream, backendKind, routeId, cancellationToken).ConfigureAwait(false);
        return await ReadBackendRouteResponseAsync(
            stream,
            backendKind,
            routeId,
            responsePacketId,
            responseCodec,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendBackendRouteNotifyCoreAsync<TNotify>(
        Stream stream,
        string backendKind,
        ushort packetId,
        TNotify notify,
        IPacketCodec<TNotify> notifyCodec,
        CancellationToken cancellationToken)
    {
        using var routedFrame = PacketCodec.Encode(
            PacketKind.Notify,
            packetId,
            RemoteDebugProtocol.SchemaVersion,
            notify,
            notifyCodec);
        var envelope = new GatewayBackendRouteEnvelope(
            backendKind,
            Guid.NewGuid(),
            PacketKind.Notify,
            packetId,
            RemoteDebugProtocol.SchemaVersion,
            routedFrame.Payload.ToArray());
        using var gatewayFrame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteEnvelope.Codec);
        await PacketFrameWriter.WriteAsync(stream, gatewayFrame, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadGatewayHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var frame = await ReadRequiredFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame.Header.Kind != PacketKind.Notify ||
            frame.Header.PacketId != Pid.GATE_HANDSHAKE_NOTIFY)
        {
            throw new InvalidOperationException("Gateway did not send the expected handshake notify packet.");
        }

        PacketCodec.Decode(frame, GatewayHandshakeNotify.Codec);
    }

    private static async Task ReadGatewayRouteAcceptedAsync(
        Stream stream,
        string backendKind,
        Guid routeId,
        CancellationToken cancellationToken)
    {
        using var frame = await ReadRequiredFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame.Header.Kind != PacketKind.Response ||
            frame.Header.PacketId != Pid.GATE_BACKEND_ROUTE)
        {
            throw new InvalidOperationException("Gateway did not send a Backend route acknowledgement.");
        }

        var response = PacketCodec.Decode(frame, GatewayBackendRouteResponse.Codec);
        if (response.RouteId != routeId)
        {
            throw new InvalidOperationException("Gateway acknowledged a different Backend route id.");
        }

        if (!string.Equals(response.BackendKind, backendKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Gateway acknowledged a different Backend kind.");
        }

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage);
        }
    }

    private static async Task<TResponse> ReadBackendRouteResponseAsync<TResponse>(
        Stream stream,
        string backendKind,
        Guid routeId,
        ushort responsePacketId,
        IPacketCodec<TResponse> responseCodec,
        CancellationToken cancellationToken)
    {
        using var frame = await ReadRequiredFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame.Header.Kind != PacketKind.Notify ||
            frame.Header.PacketId != Pid.GATE_BACKEND_ROUTE ||
            frame.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
        {
            throw new InvalidOperationException("Gateway did not send the expected Backend route response envelope.");
        }

        var envelope = PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec);
        if (!string.Equals(envelope.BackendKind, backendKind, StringComparison.Ordinal) ||
            envelope.RouteId != routeId ||
            envelope.RoutedKind != PacketKind.Response)
        {
            throw new InvalidOperationException("Backend route response envelope did not match the pending request.");
        }

        using var routedFrame = envelope.CreateRoutedFrame();
        if (envelope.RoutedPacketId == RemoteDebugPacketIds.BackendErrorResponse)
        {
            var error = PacketCodec.Decode(routedFrame, RemoteDebugBackendErrorResponse.Codec);
            throw new InvalidOperationException(error.ErrorMessage);
        }

        if (envelope.RoutedPacketId != responsePacketId ||
            envelope.RoutedVersion != RemoteDebugProtocol.SchemaVersion)
        {
            throw new InvalidOperationException("Backend route response packet did not match the expected response.");
        }

        return PacketCodec.Decode(routedFrame, responseCodec);
    }

    private static async Task<PacketFrame> ReadRequiredFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var frame = await PacketFrameReader.ReadAsync(
            stream,
            PacketReadPolicy.TrustedServer,
            cancellationToken).ConfigureAwait(false);
        return frame ?? throw new EndOfStreamException("Gateway connection closed before the expected response arrived.");
    }

    private static void ValidateRegisterResponse(
        RemoteDebugGatewayClientOptions options,
        RemoteDebugBackendClientRegisterResponse response)
    {
        if (!string.Equals(response.ClientId, options.ClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RemoteDebug Backend registered a different client id.");
        }

        if ((response.EnabledCapabilities & ~options.Capabilities) != 0)
        {
            throw new InvalidOperationException("RemoteDebug Backend enabled capabilities that the client did not request.");
        }
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion))
        {
            if (task != await Task.WhenAny(task, completion.Task).ConfigureAwait(false))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        await task.ConfigureAwait(false);
    }

    private void ValidateSessionResponse(string clientId, string sessionId)
    {
        if (!string.Equals(clientId, Session.ClientId, StringComparison.Ordinal) ||
            !string.Equals(sessionId, Session.SessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RemoteDebug Backend responded for a different client session.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref m_Disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RemoteDebugGatewayClient));
        }
    }
}
