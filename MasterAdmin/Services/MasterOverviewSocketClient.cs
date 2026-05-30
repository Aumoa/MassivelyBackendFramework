using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Collections.Concurrent;
using MasterAdmin.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Options;
using PacketCore;

namespace MasterAdmin.Services;

public sealed class MasterOverviewSocketClient(
    IOptions<MasterConnectionOptions> options,
    ILogger<MasterOverviewSocketClient> logger) : IHostedService, IMasterOverviewProvider
{
    private readonly MasterConnectionOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_StateSync = new();
    private readonly SemaphoreSlim m_WriteLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ServiceAdminStatusResponse>> m_PendingStatusRequests = [];
    private MasterOverviewState m_State = CreateInitialState(options.Value);
    private Task? m_RunTask;
    private Stream? m_ActiveStream;

    public event Action<MasterOverviewState>? StateChanged;

    public MasterOverviewState GetState()
    {
        lock (m_StateSync)
        {
            return m_State;
        }
    }

    public async Task<ServiceAdminStatusResponse> RequestServiceAdminStatusAsync(
        string targetConnectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId))
        {
            throw new ArgumentException("Target connection id is required.", nameof(targetConnectionId));
        }

        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master overview socket is not connected.");
        var request = new ServiceAdminStatusRequest(Guid.NewGuid(), targetConnectionId);
        var completion = new TaskCompletionSource<ServiceAdminStatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingStatusRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate service admin status request id was generated.");
        }

        try
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                MasterControlPacketIds.ServiceAdminStatusRequest,
                MasterControlProtocol.SchemaVersion,
                request,
                ServiceAdminStatusRequest.Codec);
            await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_WriteLock.Release();
            }

            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_PendingStatusRequests.TryRemove(request.RequestId, out _);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            PublishState(MasterOverviewConnectionState.Disabled);
            logger.LogInformation("MasterAdmin overview socket connection is disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        m_RunTask = RunAsync(m_Shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        PublishState(MasterOverviewConnectionState.Stopping);
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
                reconnectReason = "Master overview socket closed.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(
                    "MasterAdmin overview handshake timed out: {Message} Endpoint={Address}:{Port}.",
                    e.Message,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (EndOfStreamException e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(
                    "MasterAdmin overview socket closed before trust was established: {Message} Endpoint={Address}:{Port}.",
                    e.Message,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (IOException e) when (e.InnerException is SocketException socketException)
            {
                reconnectReason = e.Message;
                logger.LogWarning(
                    "MasterAdmin overview socket disconnected. SocketError={SocketError}, Endpoint={Address}:{Port}.",
                    socketException.SocketErrorCode,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (SocketException e)
            {
                reconnectReason = e.SocketErrorCode == SocketError.ConnectionRefused
                    ? "Master socket is not accepting connections."
                    : e.Message;
                logger.LogWarning(
                    "MasterAdmin overview socket unavailable. SocketError={SocketError}, Endpoint={Address}:{Port}.",
                    e.SocketErrorCode,
                    m_Options.IPAddress,
                    m_Options.Port);
            }
            catch (Exception e)
            {
                reconnectReason = e.Message;
                logger.LogWarning(e, "MasterAdmin overview socket session ended before it could be maintained.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var reconnectDelay = TimeSpan.FromMilliseconds(Math.Max(1, m_Options.ReconnectDelayMilliseconds));
            PublishState(
                MasterOverviewConnectionState.Reconnecting,
                lastError: reconnectReason,
                nextReconnectAt: DateTimeOffset.UtcNow.Add(reconnectDelay));

            try
            {
                await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var address = IPAddress.Parse(m_Options.IPAddress);
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;

        PublishState(MasterOverviewConnectionState.Connecting);
        await socket.ConnectAsync(new IPEndPoint(address, m_Options.Port), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("MasterAdmin connected to Master overview socket at {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);

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
            logger.LogInformation(
                "MasterAdmin overview socket trusted. NodeId={NodeId}, MasterConnectionId={ConnectionId}.",
                accepted.NodeId,
                accepted.ConnectionId);

            m_ActiveStream = activeStream;
            await ReceiveOverviewSnapshotsAsync(activeStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_ActiveStream = null;
            FailPendingStatusRequests(new IOException("Master overview socket closed."));

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
            PublishState(MasterOverviewConnectionState.Handshaking);
            using var challengeFrame = await ReadRequiredHandshakeFrameAsync(
                stream,
                MasterControlPacketIds.NodeAuthChallenge,
                handshakeTimeout.Token).ConfigureAwait(false);
            var challenge = PacketCodec.Decode(challengeFrame, NodeAuthChallenge.Codec);

            var hello = new NodeHello(
                MasterNodeKind.MasterAdmin,
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
                throw new InvalidOperationException("Master accepted a different node id than MasterAdmin requested.");
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

    private async Task ReceiveOverviewSnapshotsAsync(Stream stream, CancellationToken cancellationToken)
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
                    frame.Header.PacketId == MasterControlPacketIds.OverviewSnapshot)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.OverviewSnapshot);
                    var overview = PacketCodec.Decode(frame, MasterOverviewSnapshot.Codec);
                    PublishState(MasterOverviewConnectionState.Connected, overview);
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.ServiceAdminStatusResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceAdminStatusResponse);
                    var response = PacketCodec.Decode(frame, ServiceAdminStatusResponse.Codec);
                    if (m_PendingStatusRequests.TryRemove(response.RequestId, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                }
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
            throw new EndOfStreamException("Master connection closed before MasterAdmin handshake completed.");
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
        if (string.IsNullOrWhiteSpace(m_Options.NodeId))
        {
            throw new InvalidOperationException("MasterConnection:NodeId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.SharedSecret))
        {
            throw new InvalidOperationException("MasterConnection:SharedSecret must be configured.");
        }
    }

    private void PublishState(
        MasterOverviewConnectionState connectionState,
        MasterOverviewSnapshot? snapshot = null,
        string? lastError = null,
        DateTimeOffset? nextReconnectAt = null)
    {
        MasterOverviewState state;
        lock (m_StateSync)
        {
            state = new MasterOverviewState(
                connectionState,
                snapshot ?? CreateEmptySnapshot(m_Options),
                lastError,
                nextReconnectAt);
            m_State = state;
        }

        StateChanged?.Invoke(state);
    }

    private static MasterOverviewState CreateInitialState(MasterConnectionOptions options)
    {
        return new MasterOverviewState(
            options.Enabled ? MasterOverviewConnectionState.Disconnected : MasterOverviewConnectionState.Disabled,
            CreateEmptySnapshot(options));
    }

    private static MasterOverviewSnapshot CreateEmptySnapshot(MasterConnectionOptions options)
    {
        return new MasterOverviewSnapshot(
            new MasterSocketEndpoint(options.IPAddress, options.Port, options.UseTls),
            [],
            DateTimeOffset.UtcNow);
    }

    private void FailPendingStatusRequests(Exception exception)
    {
        foreach (var request in m_PendingStatusRequests.ToArray())
        {
            if (m_PendingStatusRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
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
