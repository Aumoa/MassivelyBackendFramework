using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Collections.Concurrent;
using MasterAdmin.Options;
using MasterServer.ControlPlane;
using MasterServer.Services;
using Microsoft.Extensions.Options;
using PacketCore;

namespace MasterAdmin.Services;

public sealed class MasterOverviewSocketClient(
    IOptions<MasterConnectionOptions> options,
    ILogger<MasterOverviewSocketClient> logger) :
    IHostedService,
    IMasterOverviewProvider,
    IServiceConnectionCredentials,
    IGatewayBackendRoutePolicy,
    IGatewayClientSecretCredentials,
    IBackendPacketManifestStore
{
    private readonly MasterConnectionOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_StateSync = new();
    private readonly SemaphoreSlim m_WriteLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ServiceAdminStatusResponse>> m_PendingStatusRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ServiceConnectionCredentialManagementResponse>> m_PendingCredentialRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GatewayBackendRoutePolicyManagementResponse>> m_PendingBackendRoutePolicyRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GatewayClientSecretCredentialManagementResponse>> m_PendingGatewayClientSecretRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BackendPacketManifestManagementResponse>> m_PendingBackendPacketManifestRequests = [];
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

    public async ValueTask<ServiceConnectionCredentialInfo[]> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestCredentialManagementAsync(
            ServiceConnectionCredentialManagementRequest.List(Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        EnsureCredentialResponseSucceeded(response);
        return response.Credentials;
    }

    public async ValueTask<ServiceConnectionCredentialCreated> CreateCredentialAsync(
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestCredentialManagementAsync(
            ServiceConnectionCredentialManagementRequest.Create(Guid.NewGuid(), input),
            cancellationToken).ConfigureAwait(false);
        EnsureCredentialResponseSucceeded(response);
        if (response.Credentials.Length != 1)
        {
            throw new InvalidOperationException("Master did not return the created credential.");
        }

        if (string.IsNullOrWhiteSpace(response.SharedSecret))
        {
            throw new InvalidOperationException("Master did not return the created credential secret.");
        }

        return new ServiceConnectionCredentialCreated(response.Credentials[0], response.SharedSecret);
    }

    public async ValueTask UpdateCredentialAsync(
        long id,
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestCredentialManagementAsync(
            ServiceConnectionCredentialManagementRequest.Update(Guid.NewGuid(), id, input),
            cancellationToken).ConfigureAwait(false);
        EnsureCredentialResponseSucceeded(response);
    }

    public async ValueTask<string> RotateSecretAsync(long id, CancellationToken cancellationToken = default)
    {
        var response = await RequestCredentialManagementAsync(
            ServiceConnectionCredentialManagementRequest.RotateSecret(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureCredentialResponseSucceeded(response);
        if (string.IsNullOrWhiteSpace(response.SharedSecret))
        {
            throw new InvalidOperationException("Master did not return the rotated credential secret.");
        }

        return response.SharedSecret;
    }

    public async ValueTask RemoveCredentialAsync(long id, CancellationToken cancellationToken = default)
    {
        var response = await RequestCredentialManagementAsync(
            ServiceConnectionCredentialManagementRequest.Remove(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureCredentialResponseSucceeded(response);
    }

    public async ValueTask<string[]> GetAllowedBackendKindsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. entries
                .Where(static entry => entry.Enabled)
                .Select(static entry => entry.BackendKind)
                .OrderBy(static backendKind => backendKind, StringComparer.Ordinal)
        ];
    }

    public async ValueTask<GatewayBackendRoutePolicyEntryInfo[]> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestGatewayBackendRoutePolicyManagementAsync(
            GatewayBackendRoutePolicyManagementRequest.List(Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayBackendRoutePolicyResponseSucceeded(response);
        return response.Entries;
    }

    public async ValueTask<GatewayBackendRoutePolicyEntryInfo> CreateEntryAsync(
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestGatewayBackendRoutePolicyManagementAsync(
            GatewayBackendRoutePolicyManagementRequest.Create(Guid.NewGuid(), input),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayBackendRoutePolicyResponseSucceeded(response);
        if (response.Entries.Length != 1)
        {
            throw new InvalidOperationException("Master did not return the created Gateway Backend route policy entry.");
        }

        return response.Entries[0];
    }

    public async ValueTask UpdateEntryAsync(
        long id,
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestGatewayBackendRoutePolicyManagementAsync(
            GatewayBackendRoutePolicyManagementRequest.Update(Guid.NewGuid(), id, input),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayBackendRoutePolicyResponseSucceeded(response);
    }

    public async ValueTask RemoveEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        var response = await RequestGatewayBackendRoutePolicyManagementAsync(
            GatewayBackendRoutePolicyManagementRequest.Remove(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayBackendRoutePolicyResponseSucceeded(response);
    }

    async ValueTask<GatewayClientSecretCredentialInfo[]> IGatewayClientSecretCredentials.GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var response = await RequestGatewayClientSecretCredentialManagementAsync(
            GatewayClientSecretCredentialManagementRequest.List(Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayClientSecretCredentialResponseSucceeded(response);
        return response.Credentials;
    }

    ValueTask<GatewayClientSecretValidationInfo[]> IGatewayClientSecretCredentials.GetActiveSecretsAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MasterAdmin does not expose Gateway client secret hashes.");
    }

    async ValueTask<GatewayClientSecretCredentialCreated> IGatewayClientSecretCredentials.CreateCredentialAsync(
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken)
    {
        var response = await RequestGatewayClientSecretCredentialManagementAsync(
            GatewayClientSecretCredentialManagementRequest.Create(Guid.NewGuid(), input),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayClientSecretCredentialResponseSucceeded(response);
        if (response.Credentials.Length != 1)
        {
            throw new InvalidOperationException("Master did not return the created Gateway client secret credential.");
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new InvalidOperationException("Master did not return the created Gateway client access token.");
        }

        return new GatewayClientSecretCredentialCreated(response.Credentials[0], response.AccessToken);
    }

    async ValueTask IGatewayClientSecretCredentials.UpdateCredentialAsync(
        long id,
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken)
    {
        var response = await RequestGatewayClientSecretCredentialManagementAsync(
            GatewayClientSecretCredentialManagementRequest.Update(Guid.NewGuid(), id, input),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayClientSecretCredentialResponseSucceeded(response);
    }

    async ValueTask<string> IGatewayClientSecretCredentials.RotateSecretAsync(long id, CancellationToken cancellationToken)
    {
        var response = await RequestGatewayClientSecretCredentialManagementAsync(
            GatewayClientSecretCredentialManagementRequest.RotateSecret(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayClientSecretCredentialResponseSucceeded(response);
        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new InvalidOperationException("Master did not return the rotated Gateway client access token.");
        }

        return response.AccessToken;
    }

    async ValueTask IGatewayClientSecretCredentials.RemoveCredentialAsync(long id, CancellationToken cancellationToken)
    {
        var response = await RequestGatewayClientSecretCredentialManagementAsync(
            GatewayClientSecretCredentialManagementRequest.Remove(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureGatewayClientSecretCredentialResponseSucceeded(response);
    }

    async ValueTask<BackendPacketManifest[]> IBackendPacketManifestStore.GetGatewayManifestsAsync(CancellationToken cancellationToken)
    {
        var manifests = await ((IBackendPacketManifestStore)this)
            .GetManifestInfosAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. manifests.Select(static info => info.Manifest)];
    }

    async ValueTask<BackendPacketManifestInfo[]> IBackendPacketManifestStore.GetManifestInfosAsync(CancellationToken cancellationToken)
    {
        var response = await RequestBackendPacketManifestManagementAsync(
            BackendPacketManifestManagementRequest.List(Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        EnsureBackendPacketManifestResponseSucceeded(response);
        return response.Manifests;
    }

    async ValueTask<BackendPacketManifestInfo?> IBackendPacketManifestStore.FindApprovedManifestAsync(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash hash,
        CancellationToken cancellationToken)
    {
        var normalizedBackendKind = BackendPacketManifest.NormalizeBackendKind(backendKind);
        var manifests = await ((IBackendPacketManifestStore)this)
            .GetManifestInfosAsync(cancellationToken)
            .ConfigureAwait(false);
        return manifests.FirstOrDefault(info =>
            info.Lifecycle is BackendPacketManifestLifecycle.Approved or BackendPacketManifestLifecycle.Deprecated &&
            info.Manifest.MatchesAdvertisement(normalizedBackendKind, manifestId, hash));
    }

    async ValueTask<BackendPacketManifestInfo> IBackendPacketManifestStore.CreateManifestAsync(
        BackendPacketManifestInput input,
        CancellationToken cancellationToken)
    {
        var response = await RequestBackendPacketManifestManagementAsync(
            BackendPacketManifestManagementRequest.Create(Guid.NewGuid(), input.Manifest, input.AuditNote),
            cancellationToken).ConfigureAwait(false);
        EnsureBackendPacketManifestResponseSucceeded(response);
        if (response.Manifests.Length != 1)
        {
            throw new InvalidOperationException("Master did not return the created Backend packet manifest.");
        }

        return response.Manifests[0];
    }

    async ValueTask IBackendPacketManifestStore.UpdateManifestAsync(
        long id,
        BackendPacketManifestInput input,
        CancellationToken cancellationToken)
    {
        var response = await RequestBackendPacketManifestManagementAsync(
            BackendPacketManifestManagementRequest.Update(Guid.NewGuid(), id, input.Manifest, input.AuditNote),
            cancellationToken).ConfigureAwait(false);
        EnsureBackendPacketManifestResponseSucceeded(response);
    }

    async ValueTask IBackendPacketManifestStore.DeprecateManifestAsync(
        long id,
        string auditNote,
        CancellationToken cancellationToken)
    {
        var response = await RequestBackendPacketManifestManagementAsync(
            BackendPacketManifestManagementRequest.Deprecate(Guid.NewGuid(), id, auditNote),
            cancellationToken).ConfigureAwait(false);
        EnsureBackendPacketManifestResponseSucceeded(response);
    }

    async ValueTask IBackendPacketManifestStore.RemoveManifestAsync(long id, CancellationToken cancellationToken)
    {
        var response = await RequestBackendPacketManifestManagementAsync(
            BackendPacketManifestManagementRequest.Remove(Guid.NewGuid(), id),
            cancellationToken).ConfigureAwait(false);
        EnsureBackendPacketManifestResponseSucceeded(response);
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

    private async Task<ServiceConnectionCredentialManagementResponse> RequestCredentialManagementAsync(
        ServiceConnectionCredentialManagementRequest request,
        CancellationToken cancellationToken)
    {
        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master overview socket is not connected.");
        var completion = new TaskCompletionSource<ServiceConnectionCredentialManagementResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingCredentialRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate credential management request id was generated.");
        }

        try
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                MasterControlPacketIds.ServiceConnectionCredentialManagementRequest,
                MasterControlProtocol.SchemaVersion,
                request,
                ServiceConnectionCredentialManagementRequest.Codec);
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
            m_PendingCredentialRequests.TryRemove(request.RequestId, out _);
        }
    }

    private async Task<GatewayBackendRoutePolicyManagementResponse> RequestGatewayBackendRoutePolicyManagementAsync(
        GatewayBackendRoutePolicyManagementRequest request,
        CancellationToken cancellationToken)
    {
        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master overview socket is not connected.");
        var completion = new TaskCompletionSource<GatewayBackendRoutePolicyManagementResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingBackendRoutePolicyRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate Gateway Backend route policy management request id was generated.");
        }

        try
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                MasterControlPacketIds.GatewayBackendRoutePolicyManagementRequest,
                MasterControlProtocol.SchemaVersion,
                request,
                GatewayBackendRoutePolicyManagementRequest.Codec);
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
            m_PendingBackendRoutePolicyRequests.TryRemove(request.RequestId, out _);
        }
    }

    private async Task<GatewayClientSecretCredentialManagementResponse> RequestGatewayClientSecretCredentialManagementAsync(
        GatewayClientSecretCredentialManagementRequest request,
        CancellationToken cancellationToken)
    {
        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master overview socket is not connected.");
        var completion = new TaskCompletionSource<GatewayClientSecretCredentialManagementResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingGatewayClientSecretRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate Gateway client secret credential management request id was generated.");
        }

        try
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                MasterControlPacketIds.GatewayClientSecretCredentialManagementRequest,
                MasterControlProtocol.SchemaVersion,
                request,
                GatewayClientSecretCredentialManagementRequest.Codec);
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
            m_PendingGatewayClientSecretRequests.TryRemove(request.RequestId, out _);
        }
    }

    private async Task<BackendPacketManifestManagementResponse> RequestBackendPacketManifestManagementAsync(
        BackendPacketManifestManagementRequest request,
        CancellationToken cancellationToken)
    {
        var stream = m_ActiveStream ?? throw new InvalidOperationException("Master overview socket is not connected.");
        var completion = new TaskCompletionSource<BackendPacketManifestManagementResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!m_PendingBackendPacketManifestRequests.TryAdd(request.RequestId, completion))
        {
            throw new InvalidOperationException("A duplicate Backend packet manifest management request id was generated.");
        }

        try
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                MasterControlPacketIds.BackendPacketManifestManagementRequest,
                MasterControlProtocol.SchemaVersion,
                request,
                BackendPacketManifestManagementRequest.Codec);
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
            m_PendingBackendPacketManifestRequests.TryRemove(request.RequestId, out _);
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
        m_RunTask = Task.Run(() => RunAsync(m_Shutdown.Token));
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
        PublishState(MasterOverviewConnectionState.Connecting);
        using var socket = await MasterEndpointResolver.ConnectTcpAsync(
            m_Options.IPAddress,
            m_Options.Port,
            cancellationToken).ConfigureAwait(false);
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
            FailPendingRequests(new IOException("Master overview socket closed."));

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
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.ServiceConnectionCredentialManagementResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceConnectionCredentialManagementResponse);
                    var response = PacketCodec.Decode(frame, ServiceConnectionCredentialManagementResponse.Codec);
                    if (m_PendingCredentialRequests.TryRemove(response.RequestId, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.GatewayBackendRoutePolicyManagementResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.GatewayBackendRoutePolicyManagementResponse);
                    var response = PacketCodec.Decode(frame, GatewayBackendRoutePolicyManagementResponse.Codec);
                    if (m_PendingBackendRoutePolicyRequests.TryRemove(response.RequestId, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.GatewayClientSecretCredentialManagementResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.GatewayClientSecretCredentialManagementResponse);
                    var response = PacketCodec.Decode(frame, GatewayClientSecretCredentialManagementResponse.Codec);
                    if (m_PendingGatewayClientSecretRequests.TryRemove(response.RequestId, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                    continue;
                }

                if (frame.Header.Kind == PacketKind.Control &&
                    frame.Header.PacketId == MasterControlPacketIds.BackendPacketManifestManagementResponse)
                {
                    MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.BackendPacketManifestManagementResponse);
                    var response = PacketCodec.Decode(frame, BackendPacketManifestManagementResponse.Codec);
                    if (m_PendingBackendPacketManifestRequests.TryRemove(response.RequestId, out var completion))
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

    private void FailPendingRequests(Exception exception)
    {
        foreach (var request in m_PendingStatusRequests.ToArray())
        {
            if (m_PendingStatusRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }

        foreach (var request in m_PendingCredentialRequests.ToArray())
        {
            if (m_PendingCredentialRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }

        foreach (var request in m_PendingBackendRoutePolicyRequests.ToArray())
        {
            if (m_PendingBackendRoutePolicyRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }

        foreach (var request in m_PendingGatewayClientSecretRequests.ToArray())
        {
            if (m_PendingGatewayClientSecretRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }

        foreach (var request in m_PendingBackendPacketManifestRequests.ToArray())
        {
            if (m_PendingBackendPacketManifestRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private static void EnsureCredentialResponseSucceeded(ServiceConnectionCredentialManagementResponse response)
    {
        if (response.Success)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "Master rejected the credential management request."
                : response.ErrorMessage);
    }

    private static void EnsureGatewayBackendRoutePolicyResponseSucceeded(GatewayBackendRoutePolicyManagementResponse response)
    {
        if (response.Success)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "Master rejected the Gateway Backend route policy management request."
                : response.ErrorMessage);
    }

    private static void EnsureGatewayClientSecretCredentialResponseSucceeded(GatewayClientSecretCredentialManagementResponse response)
    {
        if (response.Success)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "Master rejected the Gateway client secret credential management request."
                : response.ErrorMessage);
    }

    private static void EnsureBackendPacketManifestResponseSucceeded(BackendPacketManifestManagementResponse response)
    {
        if (response.Success)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "Master rejected the Backend packet manifest management request."
                : response.ErrorMessage);
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
