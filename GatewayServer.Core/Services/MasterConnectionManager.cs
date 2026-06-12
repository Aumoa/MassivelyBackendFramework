using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Collections.Concurrent;
using GatewayServer.ControlPlane;
using GatewayServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal sealed class MasterConnectionManager(
    IOptions<MasterConnectionOptions> options,
    IDedicatedNodeCatalogWriter dedicatedNodeCatalog,
    IBackendNodeCatalogWriter backendNodeCatalog,
    IServiceProvider serviceProvider,
    ILogger<MasterConnectionManager> logger) : IHostedService, IMasterConnectionStatusProvider, IDirectConnectCodeIssuer
{
    private readonly MasterConnectionOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_StatusSync = new();
    private readonly SemaphoreSlim m_WriteLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DirectConnectCodeResponse>> m_PendingDirectConnectCodeRequests = [];
    private MasterConnectionStatus m_Status = CreateInitialStatus(options.Value);
    private Task? m_RunTask;
    private Stream? m_ActiveStream;
    private int m_Trusted;

    public bool IsTrusted => Volatile.Read(ref m_Trusted) == 1;

    public event Action<MasterConnectionStatus>? StatusChanged;

    public MasterConnectionStatus GetStatus()
    {
        lock (m_StatusSync)
        {
            return m_Status;
        }
    }

    public async Task<DirectConnectCodeResponse> RequestDirectConnectCodeAsync(
        MasterNodeKind targetNodeKind,
        string targetMasterConnectionId,
        CancellationToken cancellationToken)
    {
        if (targetNodeKind is not (MasterNodeKind.Dedicated or MasterNodeKind.Backend))
        {
            throw new ArgumentOutOfRangeException(nameof(targetNodeKind));
        }

        if (string.IsNullOrWhiteSpace(targetMasterConnectionId))
        {
            throw new ArgumentException("Target Master connection id is required.", nameof(targetMasterConnectionId));
        }

        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master control-plane connection is not trusted.");
        var request = new DirectConnectCodeRequest(Guid.NewGuid(), targetNodeKind, targetMasterConnectionId);
        var completion = new TaskCompletionSource<DirectConnectCodeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingDirectConnectCodeRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate direct connect code request id was generated.");
        }

        try
        {
            await WriteControlAsync(
                stream,
                MasterControlPacketIds.DirectConnectCodeRequest,
                request,
                DirectConnectCodeRequest.Codec,
                cancellationToken).ConfigureAwait(false);

            var response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException(response.ErrorMessage);
            }

            return response;
        }
        finally
        {
            m_PendingDirectConnectCodeRequests.TryRemove(request.RequestId, out _);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            PublishStatus(MasterConnectionState.Disabled);
            logger.LogInformation("Gateway Master control-plane connection is disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        m_RunTask = Task.Run(() => RunAsync(m_Shutdown.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        PublishStatus(MasterConnectionState.Stopping);
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
            string? reconnectReason = null;

            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
                reconnectReason = "Master connection closed.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(
                    "Gateway Master handshake timed out: {Message} Endpoint={Address}:{Port}. Verify Master is restarted with NodeAuthChallenge support and matching MasterSocket:NodeAuthSecret.",
                    e.Message,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (EndOfStreamException e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(
                    "Gateway Master connection closed before trust was established: {Message} Endpoint={Address}:{Port}.",
                    e.Message,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (Exception e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(e, "Gateway Master control-plane session ended before trust was maintained.");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    var reconnectDelay = TimeSpan.FromMilliseconds(Math.Max(1, m_Options.ReconnectDelayMilliseconds));
                    PublishStatus(
                        MasterConnectionState.Reconnecting,
                        lastError: reconnectReason,
                        nextReconnectAt: DateTimeOffset.UtcNow.Add(reconnectDelay));
                }
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
        PublishStatus(MasterConnectionState.Connecting);
        using var socket = await MasterEndpointResolver.ConnectTcpAsync(
            m_Options.IPAddress,
            m_Options.Port,
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Gateway connected to Master socket at {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);
        PublishStatus(
            MasterConnectionState.Handshaking,
            handshakeStep: "NodeAuthChallenge",
            markConnected: true);

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
            PublishStatus(
                MasterConnectionState.Trusted,
                masterConnectionId: accepted.ConnectionId,
                markTrusted: true);
            logger.LogInformation(
                "Gateway Master control-plane session trusted. NodeId={NodeId}, MasterConnectionId={ConnectionId}.",
                accepted.NodeId,
                accepted.ConnectionId);

            m_ActiveStream = activeStream;
            await DrainTrustedFramesAsync(activeStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_ActiveStream = null;
            FailPendingDirectConnectCodeRequests(new IOException("Master control-plane connection closed."));

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
            PublishStatus(MasterConnectionState.Handshaking, handshakeStep: handshakeStep);
            using var challengeFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAuthChallenge,
                handshakeTimeout.Token).ConfigureAwait(false);
            var challenge = PacketCodec.Decode(challengeFrame, NodeAuthChallenge.Codec);

            var hello = new NodeHello(
                MasterNodeKind.Gateway,
                m_Options.NodeId,
                m_Options.DisplayName,
                MasterControlProtocol.SchemaVersion);

            handshakeStep = "NodeHello";
            PublishStatus(MasterConnectionState.Handshaking, handshakeStep: handshakeStep);
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
            PublishStatus(MasterConnectionState.Handshaking, handshakeStep: handshakeStep);
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
            PublishStatus(MasterConnectionState.Handshaking, handshakeStep: handshakeStep);
            using var acceptedFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAccepted,
                handshakeTimeout.Token).ConfigureAwait(false);
            var accepted = PacketCodec.Decode(acceptedFrame, NodeAccepted.Codec);

            if (!string.Equals(accepted.NodeId, hello.NodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Master accepted a different node id than the Gateway requested.");
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
            throw new EndOfStreamException("Master connection closed before Gateway handshake completed.");
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
                    frame.Header.PacketId == MasterControlPacketIds.DedicatedNodeSnapshot)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DedicatedNodeSnapshot);
                    var snapshot = PacketCodec.Decode(frame, DedicatedNodeSnapshot.Codec);
                    dedicatedNodeCatalog.Publish(snapshot);
                    logger.LogInformation("Gateway received Dedicated discovery snapshot. DedicatedCount={Count}.", snapshot.Nodes.Length);
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.BackendNodeSnapshot)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.BackendNodeSnapshot);
                    var snapshot = PacketCodec.Decode(frame, BackendNodeSnapshot.Codec);
                    backendNodeCatalog.Publish(snapshot);
                    logger.LogInformation(
                        "Gateway received backend discovery snapshot. BackendCount={Count}, BackendKinds={BackendKinds}.",
                        snapshot.Nodes.Length,
                        string.Join(", ", snapshot.Nodes
                            .Select(static node => node.BackendKind)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(static backendKind => backendKind, StringComparer.Ordinal)));
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.ServiceAdminStatusRequest)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceAdminStatusRequest);
                    var request = PacketCodec.Decode(frame, ServiceAdminStatusRequest.Codec);
                    await WriteServiceAdminStatusResponseAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.DirectConnectCodeResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DirectConnectCodeResponse);
                    var response = PacketCodec.Decode(frame, DirectConnectCodeResponse.Codec);
                    if (m_PendingDirectConnectCodeRequests.TryRemove(response.RequestId, out var completion))
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
        var status = GetStatus();
        var items = new List<ServiceAdminStatusItem>
        {
            new("Master", "State", status.State.ToString()),
            new("Master", "Trusted", status.IsTrusted ? "Yes" : "No"),
            new("Master", "Endpoint", status.Endpoint),
            new("Master", "Last changed", status.LastChangedAt.LocalDateTime.ToString("O"))
        };

        if (status.LastConnectedAt.HasValue)
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last connected", status.LastConnectedAt.Value.LocalDateTime.ToString("O")));
        }

        if (status.LastTrustedAt.HasValue)
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last trusted", status.LastTrustedAt.Value.LocalDateTime.ToString("O")));
        }

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            items.Add(new ServiceAdminStatusItem("Master", "Last error", status.LastError));
        }

        items.AddRange(serviceProvider.GetRequiredService<IDedicatedConnectionStatusProvider>().GetStatusItems());

        var response = new ServiceAdminStatusResponse(
            request.RequestId,
            success: true,
            MasterNodeKind.Gateway,
            status.NodeId,
            status.DisplayName,
            status.MasterConnectionId ?? request.TargetConnectionId,
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

    private void FailPendingDirectConnectCodeRequests(Exception exception)
    {
        foreach (var pair in m_PendingDirectConnectCodeRequests.ToArray())
        {
            if (m_PendingDirectConnectCodeRequests.TryRemove(pair.Key, out var completion))
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
    }

    private void PublishStatus(
        MasterConnectionState state,
        string? masterConnectionId = null,
        string? handshakeStep = null,
        string? lastError = null,
        DateTimeOffset? nextReconnectAt = null,
        bool markConnected = false,
        bool markTrusted = false)
    {
        var now = DateTimeOffset.UtcNow;
        MasterConnectionStatus status;

        lock (m_StatusSync)
        {
            var current = m_Status;
            var isTrusted = state == MasterConnectionState.Trusted;
            status = new MasterConnectionStatus(
                state,
                m_Options.Enabled,
                isTrusted,
                GetEndpoint(m_Options),
                m_Options.NodeId,
                m_Options.DisplayName,
                isTrusted ? masterConnectionId : null,
                state == MasterConnectionState.Handshaking ? handshakeStep : null,
                state is MasterConnectionState.Connecting or MasterConnectionState.Handshaking or MasterConnectionState.Trusted
                    ? null
                    : lastError,
                now,
                markConnected ? now : current.LastConnectedAt,
                markTrusted ? now : current.LastTrustedAt,
                state == MasterConnectionState.Reconnecting ? nextReconnectAt : null);
            m_Status = status;
        }

        Volatile.Write(ref m_Trusted, status.IsTrusted ? 1 : 0);
        serviceProvider.GetRequiredService<IGatewayMasterConnectionIdentitySink>()
            .SetMasterConnectionId(status.IsTrusted ? status.MasterConnectionId : null);
        StatusChanged?.Invoke(status);
    }

    private static MasterConnectionStatus CreateInitialStatus(MasterConnectionOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        return new MasterConnectionStatus(
            options.Enabled ? MasterConnectionState.Disconnected : MasterConnectionState.Disabled,
            options.Enabled,
            isTrusted: false,
            GetEndpoint(options),
            options.NodeId,
            options.DisplayName,
            masterConnectionId: null,
            handshakeStep: null,
            lastError: null,
            now,
            lastConnectedAt: null,
            lastTrustedAt: null,
            nextReconnectAt: null);
    }

    private static string GetEndpoint(MasterConnectionOptions options)
    {
        return $"{options.IPAddress}:{options.Port}";
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
