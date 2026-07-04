using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Collections.Concurrent;
using BackendServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace BackendServer.Services;

internal sealed class MasterConnectionManager(
    IOptions<MasterConnectionOptions> options,
    IOptions<GatewayListenerOptions> gatewayListenerOptions,
    IServiceProvider serviceProvider,
    ILogger<MasterConnectionManager> logger) : IHostedService, IDirectConnectCodeValidator
{
    private readonly MasterConnectionOptions m_Options = options.Value;
    private readonly GatewayListenerOptions m_GatewayListenerOptions = gatewayListenerOptions.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_StatusSync = new();
    private readonly SemaphoreSlim m_WriteLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DirectConnectCodeValidationResponse>> m_PendingDirectConnectCodeValidationRequests = [];
    private Task? m_RunTask;
    private Stream? m_ActiveStream;
    private string m_State = "Disconnected";
    private string? m_MasterConnectionId;
    private string? m_LastError;
    private DateTimeOffset m_LastChangedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? m_LastConnectedAt;
    private DateTimeOffset? m_LastTrustedAt;

    public async Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
        string code,
        string gatewayNodeId,
        string gatewayMasterConnectionId,
        CancellationToken cancellationToken)
    {
        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master control-plane connection is not trusted.");
        var request = new DirectConnectCodeValidationRequest(
            Guid.NewGuid(),
            code,
            gatewayNodeId,
            gatewayMasterConnectionId);
        var completion = new TaskCompletionSource<DirectConnectCodeValidationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingDirectConnectCodeValidationRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate direct connect code validation request id was generated.");
        }

        try
        {
            await WriteControlAsync(
                stream,
                MasterControlPacketIds.DirectConnectCodeValidationRequest,
                request,
                DirectConnectCodeValidationRequest.Codec,
                cancellationToken).ConfigureAwait(false);

            var response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new UnauthorizedAccessException(response.ErrorMessage);
            }

            return response;
        }
        finally
        {
            m_PendingDirectConnectCodeValidationRequests.TryRemove(request.RequestId, out _);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            SetStatus("Disabled");
            logger.LogInformation("Backend Master control-plane connection is disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        m_RunTask = Task.Run(() => RunAsync(m_Shutdown.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await m_Shutdown.CancelAsync().ConfigureAwait(false);

        if (m_RunTask != null)
        {
            await WaitForShutdownAsync(m_RunTask, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
                SetStatus("Disconnected");
                logger.LogInformation("Backend Master control-plane connection closed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException e)
            {
                SetStatus("Reconnecting", lastError: e.Message);
                logger.LogWarning(
                    "Backend Master handshake timed out: {Message} Endpoint={Address}:{Port}.",
                    e.Message,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (Exception e)
            {
                SetStatus("Reconnecting", lastError: e.Message);
                logger.LogWarning(
                    e,
                    "Backend Master control-plane session ended before it could be maintained. Endpoint={Address}:{Port}.",
                    m_Options.IPAddress,
                    m_Options.Port);
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

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        SetStatus("Connecting");
        using var socket = await MasterEndpointResolver.ConnectTcpAsync(
            m_Options.IPAddress,
            m_Options.Port,
            cancellationToken).ConfigureAwait(false);
        SetStatus("Handshaking", markConnected: true);
        logger.LogInformation("Backend connected to Master socket at {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);

        await using var networkStream = new NetworkStream(socket, ownsSocket: false);
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

            var accepted = await CompleteHandshakeAsync(activeStream, cancellationToken).ConfigureAwait(false);
            SetStatus("Trusted", masterConnectionId: accepted.ConnectionId, markTrusted: true);
            logger.LogInformation(
                "Backend Master control-plane session trusted. NodeId={NodeId}, MasterConnectionId={ConnectionId}.",
                accepted.NodeId,
                accepted.ConnectionId);

            m_ActiveStream = activeStream;
            await AdvertiseBackendEndpointAsync(activeStream, cancellationToken).ConfigureAwait(false);
            await DrainTrustedFramesAsync(activeStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_ActiveStream = null;
            FailPendingDirectConnectCodeValidationRequests(new IOException("Master control-plane connection closed."));

            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<NodeAccepted> CompleteHandshakeAsync(Stream stream, CancellationToken cancellationToken)
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
            var challenge = PacketCodec.Decode(challengeFrame, NodeAuthChallenge.Codec);

            var hello = new NodeHello(
                MasterNodeKind.Backend,
                m_Options.NodeId,
                m_Options.DisplayName,
                MasterControlProtocol.SchemaVersion);

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

            handshakeStep = "NodeAuthProof";
            var proof = new NodeAuthProof(
                hello.NodeId,
                MasterNodeAuthenticator.ComputeProof(challenge, hello, m_Options.SharedSecret));
            using (var proofFrame = PacketCodec.Encode(
                       PacketKind.Control,
                       MasterControlPacketIds.NodeAuthProof,
                       MasterControlProtocol.SchemaVersion,
                       proof,
                       NodeAuthProof.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, proofFrame, handshakeTimeout.Token).ConfigureAwait(false);
            }

            handshakeStep = "NodeAccepted";
            using var acceptedFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAccepted,
                handshakeTimeout.Token).ConfigureAwait(false);
            var accepted = PacketCodec.Decode(acceptedFrame, NodeAccepted.Codec);
            if (!string.Equals(accepted.NodeId, hello.NodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Master accepted a different node id than the Backend requested.");
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

    private async Task AdvertiseBackendEndpointAsync(Stream stream, CancellationToken cancellationToken)
    {
        var advertise = new BackendEndpointAdvertise(
            m_Options.BackendKind,
            new MasterSocketEndpoint(
                m_GatewayListenerOptions.IPAddress,
                m_GatewayListenerOptions.Port,
                m_GatewayListenerOptions.UseTls),
            new BackendPacketManifestId(m_Options.BackendPacketManifestId),
            new BackendPacketManifestHash(m_Options.BackendPacketManifestHash));
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendEndpointAdvertise,
            MasterControlProtocol.SchemaVersion,
            advertise,
            BackendEndpointAdvertise.Codec);
        await WriteFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Backend advertised Gateway endpoint to Master. BackendKind={BackendKind}, Endpoint={Address}:{Port}, UseTls={UseTls}, ManifestId={ManifestId}, ManifestHash={ManifestHash}.",
            advertise.BackendKind,
            advertise.GatewayEndpoint.IPAddress,
            advertise.GatewayEndpoint.Port,
            advertise.GatewayEndpoint.UseTls,
            advertise.ManifestId.Value,
            advertise.ManifestHash.Value);
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
            throw new EndOfStreamException("Master connection closed before Backend handshake completed.");
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

    private async Task DrainTrustedFramesAsync(Stream stream, CancellationToken cancellationToken)
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
                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.ServiceAdminStatusRequest)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceAdminStatusRequest);
                    var request = PacketCodec.Decode(frame, ServiceAdminStatusRequest.Codec);
                    await WriteServiceAdminStatusResponseAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.DirectConnectCodeValidationResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DirectConnectCodeValidationResponse);
                    var response = PacketCodec.Decode(frame, DirectConnectCodeValidationResponse.Codec);
                    if (m_PendingDirectConnectCodeValidationRequests.TryRemove(response.RequestId, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                }
            }
        }
    }

    private async Task WriteServiceAdminStatusResponseAsync(
        Stream stream,
        ServiceAdminStatusRequest request,
        CancellationToken cancellationToken)
    {
        string state;
        string? masterConnectionId;
        string? lastError;
        DateTimeOffset lastChangedAt;
        DateTimeOffset? lastConnectedAt;
        DateTimeOffset? lastTrustedAt;

        lock (m_StatusSync)
        {
            state = m_State;
            masterConnectionId = m_MasterConnectionId;
            lastError = m_LastError;
            lastChangedAt = m_LastChangedAt;
            lastConnectedAt = m_LastConnectedAt;
            lastTrustedAt = m_LastTrustedAt;
        }

        var items = new List<ServiceAdminStatusItem>
        {
            new("Master", "State", state),
            new("Master", "Endpoint", $"{m_Options.IPAddress}:{m_Options.Port}"),
            new("Master", "Last changed", lastChangedAt.LocalDateTime.ToString("O"))
        };

        if (lastConnectedAt.HasValue)
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last connected", lastConnectedAt.Value.LocalDateTime.ToString("O")));
        }

        if (lastTrustedAt.HasValue)
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last trusted", lastTrustedAt.Value.LocalDateTime.ToString("O")));
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last error", lastError));
        }

        items.AddRange(serviceProvider.GetRequiredService<IGatewayConnectionStatusProvider>().GetStatusItems());

        var response = new ServiceAdminStatusResponse(
            request.RequestId,
            success: true,
            MasterNodeKind.Backend,
            m_Options.NodeId,
            m_Options.DisplayName,
            masterConnectionId ?? request.TargetConnectionId,
            [.. items],
            string.Empty,
            DateTimeOffset.UtcNow);
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.ServiceAdminStatusResponse,
            MasterControlProtocol.SchemaVersion,
            response,
            ServiceAdminStatusResponse.Codec);
        await WriteFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteControlAsync<TPacket>(
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
        await WriteFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_WriteLock.Release();
        }
    }

    private void FailPendingDirectConnectCodeValidationRequests(Exception exception)
    {
        foreach (var pair in m_PendingDirectConnectCodeValidationRequests.ToArray())
        {
            if (m_PendingDirectConnectCodeValidationRequests.TryRemove(pair.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_Options.NodeId))
        {
            throw new InvalidOperationException("MasterConnection:NodeId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.SharedSecret))
        {
            throw new InvalidOperationException("MasterConnection:SharedSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.BackendKind))
        {
            throw new InvalidOperationException("MasterConnection:BackendKind must be configured.");
        }

        _ = new BackendPacketManifestId(m_Options.BackendPacketManifestId);
        _ = new BackendPacketManifestHash(m_Options.BackendPacketManifestHash);
    }

    private void SetStatus(
        string state,
        string? masterConnectionId = null,
        string? lastError = null,
        bool markConnected = false,
        bool markTrusted = false)
    {
        var now = DateTimeOffset.UtcNow;
        lock (m_StatusSync)
        {
            m_State = state;
            m_LastChangedAt = now;
            m_LastError = state is "Connecting" or "Handshaking" or "Trusted"
                ? null
                : lastError;

            if (masterConnectionId != null)
            {
                m_MasterConnectionId = masterConnectionId;
            }

            if (markConnected)
            {
                m_LastConnectedAt = now;
            }

            if (markTrusted)
            {
                m_LastTrustedAt = now;
            }
        }
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
