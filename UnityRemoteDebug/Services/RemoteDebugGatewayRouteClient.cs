using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using GatewayServer.Protocols;
using Microsoft.Extensions.Options;
using PacketCore;
using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Options;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugGatewayRouteClient(
    IOptions<GatewayBackendRouteOptions> options,
    ILogger<RemoteDebugGatewayRouteClient> logger)
{
    private readonly GatewayBackendRouteOptions m_Options = options.Value;

    public async Task<RemoteDebugBackendStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        return await SendRequestAsync(
            RemoteDebugPacketIds.BackendStatusRequest,
            RemoteDebugPacketIds.BackendStatusResponse,
            RemoteDebugBackendStatusRequest.Instance,
            RemoteDebugBackendStatusRequest.Codec,
            RemoteDebugBackendStatusResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteDebugBackendClientListResponse> ListClientsAsync(CancellationToken cancellationToken)
    {
        return await SendRequestAsync(
            RemoteDebugPacketIds.BackendClientListRequest,
            RemoteDebugPacketIds.BackendClientListResponse,
            RemoteDebugBackendClientListRequest.Instance,
            RemoteDebugBackendClientListRequest.Codec,
            RemoteDebugBackendClientListResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        ushort requestPacketId,
        ushort responsePacketId,
        TRequest request,
        IPacketCodec<TRequest> requestCodec,
        IPacketCodec<TResponse> responseCodec,
        CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            throw new InvalidOperationException("Unity RemoteDebug Gateway Backend route client is disabled.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, m_Options.RequestTimeoutMilliseconds)));

        using var tcpClient = new TcpClient
        {
            NoDelay = true
        };
        await tcpClient.ConnectAsync(m_Options.IPAddress, m_Options.Port, timeout.Token).ConfigureAwait(false);
        await using var networkStream = tcpClient.GetStream();

        SslStream? sslStream = null;
        Stream activeStream = networkStream;
        try
        {
            if (m_Options.UseTls)
            {
                sslStream = new SslStream(networkStream, leaveInnerStreamOpen: true);
                await sslStream.AuthenticateAsClientAsync(
                    m_Options.ServerName,
                    clientCertificates: null,
                    enabledSslProtocols: SslProtocols.Tls13,
                    checkCertificateRevocation: true).ConfigureAwait(false);
                activeStream = sslStream;
            }

            await ReadGatewayHandshakeAsync(activeStream, timeout.Token).ConfigureAwait(false);

            var routeId = Guid.NewGuid();
            using (var routedFrame = PacketCodec.Encode(
                       PacketKind.Request,
                       requestPacketId,
                       RemoteDebugProtocol.SchemaVersion,
                       request,
                       requestCodec))
            {
                var requestEnvelope = new GatewayBackendRouteEnvelope(
                    m_Options.BackendKind,
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
                await PacketFrameWriter.WriteAsync(activeStream, gatewayFrame, timeout.Token).ConfigureAwait(false);
            }

            await ReadGatewayRouteAcceptedAsync(activeStream, routeId, timeout.Token).ConfigureAwait(false);
            return await ReadBackendRouteResponseAsync(
                activeStream,
                routeId,
                responsePacketId,
                responseCodec,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while routing Unity RemoteDebug Backend packet {requestPacketId} through Gateway after {Math.Max(1, m_Options.RequestTimeoutMilliseconds)} ms.");
        }
        finally
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
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

    private async Task ReadGatewayRouteAcceptedAsync(
        Stream stream,
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

        if (!string.Equals(response.BackendKind, m_Options.BackendKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Gateway acknowledged a different Backend kind.");
        }

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage);
        }
    }

    private async Task<TResponse> ReadBackendRouteResponseAsync<TResponse>(
        Stream stream,
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
        if (!string.Equals(envelope.BackendKind, m_Options.BackendKind, StringComparison.Ordinal) ||
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

        logger.LogDebug(
            "Received Unity RemoteDebug Backend route response. BackendKind={BackendKind}, RouteId={RouteId}, PacketId={PacketId}.",
            envelope.BackendKind,
            envelope.RouteId,
            envelope.RoutedPacketId);
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
}
