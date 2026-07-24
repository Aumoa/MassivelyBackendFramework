using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MasterServer.ControlPlane;
using MasterServer.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace MasterServer.Services;

internal sealed class ConnectionManager(
    IOptions<MasterSocketOptions> options,
    IOptions<ServiceConnectionCredentialOptions> serviceCredentialOptions,
    INodeAuthSecretProvider nodeAuthSecretProvider,
    IDirectConnectCodeStore directConnectCodeStore,
    IServiceConnectionCredentials serviceConnectionCredentials,
    IGatewayBackendRoutePolicy gatewayBackendRoutePolicy,
    IGatewayClientSecretCredentials gatewayClientSecretCredentials,
    IBackendPacketManifestStore backendPacketManifestStore,
    ILogger<ConnectionManager> logger) : IHostedService, IConnectionManager
{
    private readonly MasterSocketOptions m_Options = options.Value;
    private readonly ServiceConnectionCredentialOptions m_ServiceCredentialOptions = serviceCredentialOptions.Value;
    private readonly IServiceConnectionCredentials m_ServiceConnectionCredentials = serviceConnectionCredentials;
    private readonly IGatewayBackendRoutePolicy m_GatewayBackendRoutePolicy = gatewayBackendRoutePolicy;
    private readonly IGatewayClientSecretCredentials m_GatewayClientSecretCredentials = gatewayClientSecretCredentials;
    private readonly IBackendPacketManifestStore m_BackendPacketManifestStore = backendPacketManifestStore;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly ConcurrentDictionary<Guid, MasterConnection> m_Connections = [];
    private readonly ConcurrentDictionary<Guid, Task> m_ConnectionTasks = [];
    private readonly ConcurrentDictionary<Guid, Guid> m_AdminStatusRequestRoutes = [];

    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private Task? m_CredentialRevalidationTask;
    private X509Certificate2? m_Cert;

    public event Action? ConnectionsChanged;

    public MasterSocketEndpoint GetSocketEndpoint()
    {
        return new MasterSocketEndpoint(m_Options.IPAddress, m_Options.Port, m_Options.UseTls);
    }

    public IReadOnlyCollection<MasterConnectionSnapshot> GetConnectionSnapshots()
    {
        return [.. m_Connections.Values
            .Select(static connection => connection.ToSnapshot())
            .OrderBy(static snapshot => snapshot.ConnectedAt)];
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        nodeAuthSecretProvider.EnsureConfigured();

        if (m_Options.UseTls)
        {
            m_Cert = await LoadCertificateAsync(m_Options, cancellationToken).ConfigureAwait(false);
        }

        var listenAddress = await MasterEndpointResolver.ResolveBindAddressAsync(
            m_Options.IPAddress,
            cancellationToken).ConfigureAwait(false);
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
        if (m_ServiceCredentialOptions.RevalidationIntervalMilliseconds > 0)
        {
            m_CredentialRevalidationTask = RevalidateCredentialsUntilStoppedAsync(m_Shutdown.Token);
        }

        logger.LogInformation("Master socket server is listening on {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await m_Shutdown.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();

        if (m_AcceptTask != null)
        {
            await WaitForShutdownAsync(m_AcceptTask, cancellationToken).ConfigureAwait(false);
        }

        if (m_CredentialRevalidationTask != null)
        {
            await WaitForShutdownAsync(m_CredentialRevalidationTask, cancellationToken).ConfigureAwait(false);
        }

        foreach (var connection in m_Connections.Values)
        {
            connection.Dispose();
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket?.Dispose();
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                socket?.Dispose();
                logger.LogError(e, "Error occurred while accepting a Master node connection.");
            }
        }
    }

    private async Task RevalidateCredentialsUntilStoppedAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(1, m_ServiceCredentialOptions.RevalidationIntervalMilliseconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                await RevalidateTrustedConnectionCredentialsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to revalidate Master node credentials.");
            }
        }
    }

    private async Task RevalidateTrustedConnectionCredentialsAsync(CancellationToken cancellationToken)
    {
        foreach (var connection in m_Connections.Values)
        {
            if (!connection.IsTrusted ||
                string.IsNullOrWhiteSpace(connection.CredentialVersion))
            {
                continue;
            }

            bool isCurrent;
            try
            {
                isCurrent = await nodeAuthSecretProvider.IsCredentialCurrentAsync(
                    connection.NodeKind,
                    connection.NodeId,
                    connection.CredentialVersion,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to revalidate Master node credential. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                    connection.ConnectionId,
                    connection.NodeKind,
                    connection.NodeId);
                continue;
            }

            if (isCurrent)
            {
                continue;
            }

            logger.LogWarning(
                "Closing Master node connection because its credential is no longer current. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                connection.ConnectionId,
                connection.NodeKind,
                connection.NodeId);
            connection.Dispose();
        }
    }

    private void StartConnection(Socket socket, CancellationToken cancellationToken)
    {
        var connection = new MasterConnection(socket);
        if (!m_Connections.TryAdd(connection.ConnectionId, connection))
        {
            socket.Dispose();
            return;
        }

        NotifyConnectionsChanged();

        var task = HandleConnectionAsync(connection, cancellationToken);
        m_ConnectionTasks.TryAdd(connection.ConnectionId, task);

        _ = task.ContinueWith(
            completed =>
            {
                m_ConnectionTasks.TryRemove(connection.ConnectionId, out _);
                m_Connections.TryRemove(connection.ConnectionId, out _);
                connection.Dispose();
                NotifyConnectionsChanged();

                if (completed.Exception != null)
                {
                    logger.LogError(completed.Exception, "Unhandled Master connection task failure.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await using var networkStream = new NetworkStream(connection.Socket, ownsSocket: false);
        SslStream? sslStream = null;
        Stream activeStream = networkStream;

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

            connection.AttachStream(activeStream);
            await AuthenticateNodeAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);

            if (connection.NodeKind == MasterNodeKind.MasterAdmin)
            {
                await PushOverviewUntilClosedAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
            }
            else if (connection.NodeKind == MasterNodeKind.Gateway)
            {
                await PushGatewayDiscoveryUntilClosedAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
            }
            else if (BackendNodeEndpoint.IsBackendNodeKind(connection.NodeKind))
            {
                await PushBackendControlSnapshotsUntilClosedAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DrainUntilClosedAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
            }

            LogNodeDisconnected(connection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e) when (IsRemoteDisconnect(e))
        {
            LogNodeDisconnectedAbruptly(connection, e);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Master node connection ended with an unexpected error. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connection.ConnectionId,
                connection.NodeKind,
                connection.NodeId,
                connection.RemoteEndPoint);
        }
        finally
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DrainUntilClosedAsync(MasterConnection connection, Stream stream, CancellationToken cancellationToken)
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
                await ProcessTrustedFrameAsync(connection, frame, cancellationToken).ConfigureAwait(false);
                connection.MarkSeen();
                NotifyConnectionsChanged();
            }
        }
    }

    private async Task ProcessTrustedFrameAsync(MasterConnection connection, PacketFrame frame, CancellationToken cancellationToken)
    {
        if (connection.NodeKind == MasterNodeKind.Dedicated &&
            frame.Header.Kind == PacketKind.Control &&
            frame.Header.PacketId == MasterControlPacketIds.DedicatedEndpointAdvertise)
        {
            MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DedicatedEndpointAdvertise);
            var advertised = PacketCodec.Decode(frame, DedicatedEndpointAdvertise.Codec);
            connection.UpdateDedicatedGatewayEndpoint(advertised.GatewayEndpoint);
            logger.LogInformation(
                "Dedicated node advertised Gateway endpoint. ConnectionId={ConnectionId}, NodeId={NodeId}, Endpoint={Address}:{Port}, UseTls={UseTls}.",
                connection.ConnectionId,
                connection.NodeId,
                advertised.GatewayEndpoint.IPAddress,
                advertised.GatewayEndpoint.Port,
                advertised.GatewayEndpoint.UseTls);
            return;
        }

        if (BackendNodeEndpoint.IsBackendNodeKind(connection.NodeKind) &&
            frame.Header.Kind == PacketKind.Control &&
            frame.Header.PacketId == MasterControlPacketIds.BackendEndpointAdvertise)
        {
            MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.BackendEndpointAdvertise);
            var advertised = PacketCodec.Decode(frame, BackendEndpointAdvertise.Codec);
            if (!string.Equals(connection.AuthorizedBackendKind, advertised.BackendKind, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Backend node '{connection.NodeId}' advertised unauthorized backend kind '{advertised.BackendKind}'.");
            }

            var approvedManifest = await m_BackendPacketManifestStore
                .FindApprovedManifestAsync(
                    advertised.BackendKind,
                    advertised.ManifestId,
                    advertised.ManifestHash,
                    cancellationToken)
                .ConfigureAwait(false);
            if (approvedManifest == null)
            {
                throw new UnauthorizedAccessException(
                    $"Backend node '{connection.NodeId}' advertised an unapproved packet manifest '{advertised.ManifestId.Value}' ({advertised.ManifestHash.Value}).");
            }

            connection.UpdateBackendGatewayEndpoint(
                connection.AuthorizedBackendKind!,
                advertised.GatewayEndpoint,
                advertised.ManifestId,
                advertised.ManifestHash,
                advertised.Descriptor,
                advertised.AuthenticationMethods);
            logger.LogInformation(
                "Backend node advertised Gateway endpoint. ConnectionId={ConnectionId}, NodeKind={NodeKind}, BackendKind={BackendKind}, NodeId={NodeId}, Endpoint={Address}:{Port}, UseTls={UseTls}, ManifestId={ManifestId}, ManifestHash={ManifestHash}, State={State}, DescriptorVersion={DescriptorVersion}, DescriptorHash={DescriptorHash}.",
                connection.ConnectionId,
                connection.NodeKind,
                connection.AuthorizedBackendKind,
                connection.NodeId,
                advertised.GatewayEndpoint.IPAddress,
                advertised.GatewayEndpoint.Port,
                advertised.GatewayEndpoint.UseTls,
                advertised.ManifestId.Value,
                advertised.ManifestHash.Value,
                advertised.Descriptor.State,
                advertised.Descriptor.DescriptorVersion,
                advertised.Descriptor.DescriptorHash);
            return;
        }

        if (frame.Header.Kind != PacketKind.Control)
        {
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.ServiceAdminStatusRequest)
        {
            await RelayServiceAdminStatusRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.ServiceAdminStatusResponse)
        {
            await RelayServiceAdminStatusResponseAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.DirectConnectCodeRequest)
        {
            await HandleDirectConnectCodeRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.DirectConnectCodeValidationRequest)
        {
            await HandleDirectConnectCodeValidationRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.ServiceConnectionCredentialManagementRequest)
        {
            await HandleServiceConnectionCredentialManagementRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.GatewayBackendRoutePolicyManagementRequest)
        {
            await HandleGatewayBackendRoutePolicyManagementRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.GatewayClientSecretCredentialManagementRequest)
        {
            await HandleGatewayClientSecretCredentialManagementRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Header.PacketId == MasterControlPacketIds.BackendPacketManifestManagementRequest)
        {
            await HandleBackendPacketManifestManagementRequestAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            return;
        }
    }

    private async Task PushOverviewUntilClosedAsync(MasterConnection connection, Stream stream, CancellationToken cancellationToken)
    {
        void OnConnectionsChanged()
        {
            _ = SendOverviewSnapshotSafeAsync();
        }

        async Task SendOverviewSnapshotSafeAsync()
        {
            try
            {
                await WriteOverviewSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e) when (IsRemoteDisconnect(e))
            {
                logger.LogDebug(
                    e,
                    "MasterAdmin overview socket write failed because the remote connection closed. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    connection.ConnectionId,
                    connection.NodeId);
                connection.Dispose();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to push Master overview snapshot. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    connection.ConnectionId,
                    connection.NodeId);
                connection.Dispose();
            }
        }

        ConnectionsChanged += OnConnectionsChanged;

        try
        {
            await WriteOverviewSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
            await DrainUntilClosedAsync(connection, stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ConnectionsChanged -= OnConnectionsChanged;
        }
    }

    private async Task WriteOverviewSnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.OverviewSnapshot,
            CreateOverviewSnapshot(),
            MasterOverviewSnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PushGatewayDiscoveryUntilClosedAsync(MasterConnection connection, Stream stream, CancellationToken cancellationToken)
    {
        void OnConnectionsChanged()
        {
            _ = SendGatewayDiscoverySnapshotsSafeAsync();
        }

        async Task SendGatewayDiscoverySnapshotsSafeAsync()
        {
            try
            {
                await WriteGatewayDiscoverySnapshotsAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e) when (IsRemoteDisconnect(e))
            {
                logger.LogDebug(
                    e,
                    "Gateway discovery socket write failed because the remote connection closed. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    connection.ConnectionId,
                    connection.NodeId);
                connection.Dispose();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to push Gateway discovery snapshots. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    connection.ConnectionId,
                    connection.NodeId);
                connection.Dispose();
            }
        }

        ConnectionsChanged += OnConnectionsChanged;

        try
        {
            await WriteGatewayDiscoverySnapshotsAsync(connection, cancellationToken).ConfigureAwait(false);
            await DrainUntilClosedAsync(connection, stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ConnectionsChanged -= OnConnectionsChanged;
        }
    }

    private async Task WriteGatewayDiscoverySnapshotsAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await WriteDedicatedNodeSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        await WriteBackendNodeSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        await WriteBackendPacketManifestSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        await WriteGatewayBackendRoutePolicySnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        await WriteGatewayClientSecretCredentialSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task PushBackendControlSnapshotsUntilClosedAsync(
        MasterConnection connection,
        Stream stream,
        CancellationToken cancellationToken)
    {
        await WriteBackendPacketManifestSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        await DrainUntilClosedAsync(connection, stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDedicatedNodeSnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.DedicatedNodeSnapshot,
            CreateDedicatedNodeSnapshot(),
            DedicatedNodeSnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendNodeSnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.BackendNodeSnapshot,
            CreateBackendNodeSnapshot(),
            BackendNodeSnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteGatewayBackendRoutePolicySnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.GatewayBackendRoutePolicySnapshot,
            await CreateGatewayBackendRoutePolicySnapshotAsync(cancellationToken).ConfigureAwait(false),
            GatewayBackendRoutePolicySnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendPacketManifestSnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.BackendPacketManifestSnapshot,
            await CreateBackendPacketManifestSnapshotAsync(cancellationToken).ConfigureAwait(false),
            BackendPacketManifestSnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteGatewayClientSecretCredentialSnapshotAsync(MasterConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.GatewayClientSecretCredentialSnapshot,
            await CreateGatewayClientSecretCredentialSnapshotAsync(cancellationToken).ConfigureAwait(false),
            GatewayClientSecretCredentialSnapshot.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task BroadcastGatewayBackendRoutePolicySnapshotAsync(CancellationToken cancellationToken)
    {
        var gateways = m_Connections.Values
            .Where(static connection => connection.IsTrusted && connection.NodeKind == MasterNodeKind.Gateway)
            .ToArray();

        foreach (var gateway in gateways)
        {
            try
            {
                await WriteGatewayBackendRoutePolicySnapshotAsync(gateway, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (IsRemoteDisconnect(e))
            {
                logger.LogDebug(
                    e,
                    "Gateway Backend route policy snapshot write failed because the remote connection closed. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    gateway.ConnectionId,
                    gateway.NodeId);
                gateway.Dispose();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to push Gateway Backend route policy snapshot. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    gateway.ConnectionId,
                    gateway.NodeId);
            }
        }
    }

    private async Task BroadcastGatewayClientSecretCredentialSnapshotAsync(CancellationToken cancellationToken)
    {
        var gateways = m_Connections.Values
            .Where(static connection => connection.IsTrusted && connection.NodeKind == MasterNodeKind.Gateway)
            .ToArray();

        foreach (var gateway in gateways)
        {
            try
            {
                await WriteGatewayClientSecretCredentialSnapshotAsync(gateway, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (IsRemoteDisconnect(e))
            {
                logger.LogDebug(
                    e,
                    "Gateway client secret credential snapshot write failed because the remote connection closed. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    gateway.ConnectionId,
                    gateway.NodeId);
                gateway.Dispose();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to push Gateway client secret credential snapshot. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    gateway.ConnectionId,
                    gateway.NodeId);
            }
        }
    }

    private async Task BroadcastBackendPacketManifestSnapshotAsync(CancellationToken cancellationToken)
    {
        var recipients = m_Connections.Values
            .Where(static connection =>
                connection.IsTrusted &&
                (connection.NodeKind == MasterNodeKind.Gateway ||
                 BackendNodeEndpoint.IsBackendNodeKind(connection.NodeKind)))
            .ToArray();

        foreach (var recipient in recipients)
        {
            try
            {
                await WriteBackendPacketManifestSnapshotAsync(recipient, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (IsRemoteDisconnect(e))
            {
                logger.LogDebug(
                    e,
                    "Backend packet manifest snapshot write failed because the remote connection closed. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    recipient.ConnectionId,
                    recipient.NodeId);
                recipient.Dispose();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Failed to push Backend packet manifest snapshot. ConnectionId={ConnectionId}, NodeId={NodeId}.",
                    recipient.ConnectionId,
                    recipient.NodeId);
            }
        }
    }

    private async Task RelayServiceAdminStatusRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceAdminStatusRequest);
        var request = PacketCodec.Decode(frame, ServiceAdminStatusRequest.Codec);

        if (source.NodeKind != MasterNodeKind.MasterAdmin)
        {
            logger.LogWarning(
                "Rejected service admin status request from non-admin node. SourceConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                source.ConnectionId,
                source.NodeKind,
                source.NodeId);
            return;
        }

        if (!Guid.TryParseExact(request.TargetConnectionId, "N", out var targetConnectionId) &&
            !Guid.TryParse(request.TargetConnectionId, out targetConnectionId))
        {
            await WriteServiceAdminFailureAsync(source, request, "Target connection id is invalid.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!m_Connections.TryGetValue(targetConnectionId, out var target) ||
            !target.IsTrusted ||
            target.NodeKind is not (MasterNodeKind.Gateway or MasterNodeKind.Dedicated or MasterNodeKind.Backend))
        {
            await WriteServiceAdminFailureAsync(source, request, "Target node is not connected or cannot provide a management page.", cancellationToken).ConfigureAwait(false);
            return;
        }

        m_AdminStatusRequestRoutes[request.RequestId] = source.ConnectionId;
        _ = ExpireAdminStatusRouteAsync(request.RequestId, m_Shutdown.Token);

        try
        {
            await target.WriteControlAsync(
                MasterControlPacketIds.ServiceAdminStatusRequest,
                request,
                ServiceAdminStatusRequest.Codec,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            m_AdminStatusRequestRoutes.TryRemove(request.RequestId, out _);
            throw;
        }
    }

    private async Task RelayServiceAdminStatusResponseAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceAdminStatusResponse);
        var response = PacketCodec.Decode(frame, ServiceAdminStatusResponse.Codec);

        if (!m_AdminStatusRequestRoutes.TryRemove(response.RequestId, out var adminConnectionId))
        {
            logger.LogDebug(
                "Dropped service admin status response with no waiting admin request. SourceConnectionId={ConnectionId}, RequestId={RequestId}.",
                source.ConnectionId,
                response.RequestId);
            return;
        }

        if (!m_Connections.TryGetValue(adminConnectionId, out var adminConnection) ||
            !adminConnection.IsTrusted ||
            adminConnection.NodeKind != MasterNodeKind.MasterAdmin)
        {
            return;
        }

        await adminConnection.WriteControlAsync(
            MasterControlPacketIds.ServiceAdminStatusResponse,
            response,
            ServiceAdminStatusResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteServiceAdminFailureAsync(
        MasterConnection connection,
        ServiceAdminStatusRequest request,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var response = ServiceAdminStatusResponse.Failure(
            request.RequestId,
            request.TargetConnectionId,
            errorMessage);
        await connection.WriteControlAsync(
            MasterControlPacketIds.ServiceAdminStatusResponse,
            response,
            ServiceAdminStatusResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDirectConnectCodeRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DirectConnectCodeRequest);
        var request = PacketCodec.Decode(frame, DirectConnectCodeRequest.Codec);

        if (source.NodeKind != MasterNodeKind.Gateway)
        {
            await WriteDirectConnectCodeFailureAsync(
                source,
                request.RequestId,
                "Only Gateway nodes can request direct connect codes.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsDirectConnectTargetNodeKind(request.TargetNodeKind))
        {
            await WriteDirectConnectCodeFailureAsync(
                source,
                request.RequestId,
                "Target node kind cannot accept direct connections.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryParseConnectionId(request.TargetMasterConnectionId, out var targetConnectionId) ||
            !m_Connections.TryGetValue(targetConnectionId, out var targetConnection) ||
            !targetConnection.IsTrusted ||
            targetConnection.NodeKind != request.TargetNodeKind)
        {
            await WriteDirectConnectCodeFailureAsync(
                source,
                request.RequestId,
                "Target node is not connected.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var ticket = await directConnectCodeStore.CreateAsync(
            source.ConnectionId.ToString("N"),
            source.NodeId,
            targetConnection.NodeKind,
            targetConnection.ConnectionId.ToString("N"),
            targetConnection.NodeId,
            cancellationToken).ConfigureAwait(false);
        var response = new DirectConnectCodeResponse(
            request.RequestId,
            success: true,
            ticket.Code,
            ticket.ExpiresAt,
            string.Empty);
        await source.WriteControlAsync(
            MasterControlPacketIds.DirectConnectCodeResponse,
            response,
            DirectConnectCodeResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDirectConnectCodeValidationRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.DirectConnectCodeValidationRequest);
        var request = PacketCodec.Decode(frame, DirectConnectCodeValidationRequest.Codec);

        if (!IsDirectConnectTargetNodeKind(source.NodeKind))
        {
            await WriteDirectConnectCodeValidationFailureAsync(
                source,
                request.RequestId,
                "Only direct-connect target nodes can validate direct connect codes.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var ticket = await directConnectCodeStore.ConsumeAsync(
            request.Code,
            request.GatewayMasterConnectionId,
            request.GatewayNodeId,
            source.NodeKind,
            source.ConnectionId.ToString("N"),
            source.NodeId,
            cancellationToken).ConfigureAwait(false);
        if (ticket == null ||
            ticket.TargetNodeKind != source.NodeKind ||
            !string.Equals(ticket.TargetMasterConnectionId, source.ConnectionId.ToString("N"), StringComparison.Ordinal) ||
            !string.Equals(ticket.GatewayMasterConnectionId, request.GatewayMasterConnectionId, StringComparison.Ordinal) ||
            !string.Equals(ticket.GatewayNodeId, request.GatewayNodeId, StringComparison.Ordinal))
        {
            await WriteDirectConnectCodeValidationFailureAsync(
                source,
                request.RequestId,
                "Direct connect code is invalid or expired.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryParseConnectionId(ticket.GatewayMasterConnectionId, out var gatewayConnectionId) ||
            !m_Connections.TryGetValue(gatewayConnectionId, out var gatewayConnection) ||
            !gatewayConnection.IsTrusted ||
            gatewayConnection.NodeKind != MasterNodeKind.Gateway ||
            !string.Equals(gatewayConnection.NodeId, ticket.GatewayNodeId, StringComparison.Ordinal))
        {
            await WriteDirectConnectCodeValidationFailureAsync(
                source,
                request.RequestId,
                "Gateway node is no longer connected.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var response = new DirectConnectCodeValidationResponse(
            request.RequestId,
            success: true,
            ticket.GatewayNodeId,
            ticket.GatewayMasterConnectionId,
            ticket.TargetNodeKind,
            ticket.TargetNodeId,
            ticket.TargetMasterConnectionId,
            string.Empty);
        await source.WriteControlAsync(
            MasterControlPacketIds.DirectConnectCodeValidationResponse,
            response,
            DirectConnectCodeValidationResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleServiceConnectionCredentialManagementRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.ServiceConnectionCredentialManagementRequest);
        var request = PacketCodec.Decode(frame, ServiceConnectionCredentialManagementRequest.Codec);

        if (source.NodeKind != MasterNodeKind.MasterAdmin)
        {
            logger.LogWarning(
                "Rejected service connection credential management request from non-admin node. SourceConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                source.ConnectionId,
                source.NodeKind,
                source.NodeId);
            return;
        }

        ServiceConnectionCredentialManagementResponse response;
        try
        {
            response = await ExecuteServiceConnectionCredentialRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Failed to process service connection credential management request. RequestId={RequestId}, Operation={Operation}.",
                request.RequestId,
                request.Operation);
            response = ServiceConnectionCredentialManagementResponse.Failure(request.RequestId, e.Message);
        }

        await source.WriteControlAsync(
            MasterControlPacketIds.ServiceConnectionCredentialManagementResponse,
            response,
            ServiceConnectionCredentialManagementResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleGatewayBackendRoutePolicyManagementRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.GatewayBackendRoutePolicyManagementRequest);
        var request = PacketCodec.Decode(frame, GatewayBackendRoutePolicyManagementRequest.Codec);

        if (source.NodeKind != MasterNodeKind.MasterAdmin)
        {
            logger.LogWarning(
                "Rejected Gateway Backend route policy management request from non-admin node. SourceConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                source.ConnectionId,
                source.NodeKind,
                source.NodeId);
            return;
        }

        GatewayBackendRoutePolicyManagementResponse response;
        try
        {
            response = await ExecuteGatewayBackendRoutePolicyRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Failed to process Gateway Backend route policy management request. RequestId={RequestId}, Operation={Operation}.",
                request.RequestId,
                request.Operation);
            response = GatewayBackendRoutePolicyManagementResponse.Failure(request.RequestId, e.Message);
        }

        await source.WriteControlAsync(
            MasterControlPacketIds.GatewayBackendRoutePolicyManagementResponse,
            response,
            GatewayBackendRoutePolicyManagementResponse.Codec,
            cancellationToken).ConfigureAwait(false);

        if (response.Success && IsGatewayBackendRoutePolicyMutation(request.Operation))
        {
            await BroadcastGatewayBackendRoutePolicySnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleGatewayClientSecretCredentialManagementRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.GatewayClientSecretCredentialManagementRequest);
        var request = PacketCodec.Decode(frame, GatewayClientSecretCredentialManagementRequest.Codec);

        if (source.NodeKind != MasterNodeKind.MasterAdmin)
        {
            logger.LogWarning(
                "Rejected Gateway client secret credential management request from non-admin node. SourceConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                source.ConnectionId,
                source.NodeKind,
                source.NodeId);
            return;
        }

        GatewayClientSecretCredentialManagementResponse response;
        try
        {
            response = await ExecuteGatewayClientSecretCredentialRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Failed to process Gateway client secret credential management request. RequestId={RequestId}, Operation={Operation}.",
                request.RequestId,
                request.Operation);
            response = GatewayClientSecretCredentialManagementResponse.Failure(request.RequestId, e.Message);
        }

        await source.WriteControlAsync(
            MasterControlPacketIds.GatewayClientSecretCredentialManagementResponse,
            response,
            GatewayClientSecretCredentialManagementResponse.Codec,
            cancellationToken).ConfigureAwait(false);

        if (response.Success && IsGatewayClientSecretCredentialMutation(request.Operation))
        {
            await BroadcastGatewayClientSecretCredentialSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleBackendPacketManifestManagementRequestAsync(
        MasterConnection source,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        MasterControlProtocol.ValidateControlFrame(frame, MasterControlPacketIds.BackendPacketManifestManagementRequest);

        if (source.NodeKind != MasterNodeKind.MasterAdmin)
        {
            logger.LogWarning(
                "Rejected Backend packet manifest management request from non-admin node. SourceConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
                source.ConnectionId,
                source.NodeKind,
                source.NodeId);
            return;
        }

        var request = PacketCodec.Decode(frame, BackendPacketManifestManagementRequest.Codec);

        BackendPacketManifestManagementResponse response;
        try
        {
            response = await ExecuteBackendPacketManifestRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Failed to process Backend packet manifest management request. RequestId={RequestId}, Operation={Operation}.",
                request.RequestId,
                request.Operation);
            response = BackendPacketManifestManagementResponse.Failure(request.RequestId, e.Message);
        }

        await source.WriteControlAsync(
            MasterControlPacketIds.BackendPacketManifestManagementResponse,
            response,
            BackendPacketManifestManagementResponse.Codec,
            cancellationToken).ConfigureAwait(false);

        if (response.Success && IsBackendPacketManifestMutation(request.Operation))
        {
            await BroadcastBackendPacketManifestSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ServiceConnectionCredentialManagementResponse> ExecuteServiceConnectionCredentialRequestAsync(
        ServiceConnectionCredentialManagementRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Operation)
        {
            case ServiceConnectionCredentialOperation.List:
            {
                var credentials = await m_ServiceConnectionCredentials.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
                return ServiceConnectionCredentialManagementResponse.SuccessResult(request.RequestId, credentials);
            }

            case ServiceConnectionCredentialOperation.Create:
            {
                var created = await m_ServiceConnectionCredentials.CreateCredentialAsync(request.ToInput(), cancellationToken).ConfigureAwait(false);
                return ServiceConnectionCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    [created.Credential],
                    created.SharedSecret);
            }

            case ServiceConnectionCredentialOperation.Update:
                await m_ServiceConnectionCredentials.UpdateCredentialAsync(
                    request.CredentialId,
                    request.ToInput(),
                    cancellationToken).ConfigureAwait(false);
                return ServiceConnectionCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<ServiceConnectionCredentialInfo>());

            case ServiceConnectionCredentialOperation.RotateSecret:
            {
                var sharedSecret = await m_ServiceConnectionCredentials.RotateSecretAsync(
                    request.CredentialId,
                    cancellationToken).ConfigureAwait(false);
                return ServiceConnectionCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<ServiceConnectionCredentialInfo>(),
                    sharedSecret);
            }

            case ServiceConnectionCredentialOperation.Remove:
                await m_ServiceConnectionCredentials.RemoveCredentialAsync(request.CredentialId, cancellationToken).ConfigureAwait(false);
                return ServiceConnectionCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<ServiceConnectionCredentialInfo>());

            default:
                throw new InvalidOperationException($"Unsupported credential management operation '{request.Operation}'.");
        }
    }

    private async ValueTask<GatewayBackendRoutePolicyManagementResponse> ExecuteGatewayBackendRoutePolicyRequestAsync(
        GatewayBackendRoutePolicyManagementRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Operation)
        {
            case GatewayBackendRoutePolicyOperation.List:
            {
                var entries = await m_GatewayBackendRoutePolicy.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
                return GatewayBackendRoutePolicyManagementResponse.SuccessResult(request.RequestId, entries);
            }

            case GatewayBackendRoutePolicyOperation.Create:
            {
                var created = await m_GatewayBackendRoutePolicy.CreateEntryAsync(request.ToInput(), cancellationToken).ConfigureAwait(false);
                return GatewayBackendRoutePolicyManagementResponse.SuccessResult(
                    request.RequestId,
                    [created]);
            }

            case GatewayBackendRoutePolicyOperation.Update:
                await m_GatewayBackendRoutePolicy.UpdateEntryAsync(
                    request.EntryId,
                    request.ToInput(),
                    cancellationToken).ConfigureAwait(false);
                return GatewayBackendRoutePolicyManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<GatewayBackendRoutePolicyEntryInfo>());

            case GatewayBackendRoutePolicyOperation.Remove:
                await m_GatewayBackendRoutePolicy.RemoveEntryAsync(request.EntryId, cancellationToken).ConfigureAwait(false);
                return GatewayBackendRoutePolicyManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<GatewayBackendRoutePolicyEntryInfo>());

            default:
                throw new InvalidOperationException($"Unsupported Gateway Backend route policy management operation '{request.Operation}'.");
        }
    }

    private async ValueTask<GatewayClientSecretCredentialManagementResponse> ExecuteGatewayClientSecretCredentialRequestAsync(
        GatewayClientSecretCredentialManagementRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Operation)
        {
            case GatewayClientSecretCredentialOperation.List:
            {
                var credentials = await m_GatewayClientSecretCredentials.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
                return GatewayClientSecretCredentialManagementResponse.SuccessResult(request.RequestId, credentials);
            }

            case GatewayClientSecretCredentialOperation.Create:
            {
                var created = await m_GatewayClientSecretCredentials.CreateCredentialAsync(request.ToInput(), cancellationToken).ConfigureAwait(false);
                return GatewayClientSecretCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    [created.Credential],
                    created.AccessToken);
            }

            case GatewayClientSecretCredentialOperation.Update:
                await m_GatewayClientSecretCredentials.UpdateCredentialAsync(
                    request.CredentialId,
                    request.ToInput(),
                    cancellationToken).ConfigureAwait(false);
                return GatewayClientSecretCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<GatewayClientSecretCredentialInfo>());

            case GatewayClientSecretCredentialOperation.RotateSecret:
            {
                var accessToken = await m_GatewayClientSecretCredentials.RotateSecretAsync(
                    request.CredentialId,
                    cancellationToken).ConfigureAwait(false);
                return GatewayClientSecretCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<GatewayClientSecretCredentialInfo>(),
                    accessToken);
            }

            case GatewayClientSecretCredentialOperation.Remove:
                await m_GatewayClientSecretCredentials.RemoveCredentialAsync(request.CredentialId, cancellationToken).ConfigureAwait(false);
                return GatewayClientSecretCredentialManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<GatewayClientSecretCredentialInfo>());

            default:
                throw new InvalidOperationException($"Unsupported Gateway client secret credential management operation '{request.Operation}'.");
        }
    }

    private async ValueTask<BackendPacketManifestManagementResponse> ExecuteBackendPacketManifestRequestAsync(
        BackendPacketManifestManagementRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Operation)
        {
            case BackendPacketManifestOperation.List:
            {
                var manifests = await m_BackendPacketManifestStore.GetManifestInfosAsync(cancellationToken).ConfigureAwait(false);
                return BackendPacketManifestManagementResponse.SuccessResult(request.RequestId, manifests);
            }

            case BackendPacketManifestOperation.Create:
            {
                var created = await m_BackendPacketManifestStore.CreateManifestAsync(request.ToInput(), cancellationToken).ConfigureAwait(false);
                return BackendPacketManifestManagementResponse.SuccessResult(
                    request.RequestId,
                    [created]);
            }

            case BackendPacketManifestOperation.Update:
                await m_BackendPacketManifestStore.UpdateManifestAsync(
                    request.ManifestRecordId,
                    request.ToInput(),
                    cancellationToken).ConfigureAwait(false);
                return BackendPacketManifestManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<BackendPacketManifestInfo>());

            case BackendPacketManifestOperation.Deprecate:
                await m_BackendPacketManifestStore.DeprecateManifestAsync(
                    request.ManifestRecordId,
                    request.AuditNote,
                    cancellationToken).ConfigureAwait(false);
                return BackendPacketManifestManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<BackendPacketManifestInfo>());

            case BackendPacketManifestOperation.Remove:
                await m_BackendPacketManifestStore.RemoveManifestAsync(request.ManifestRecordId, cancellationToken).ConfigureAwait(false);
                return BackendPacketManifestManagementResponse.SuccessResult(
                    request.RequestId,
                    Array.Empty<BackendPacketManifestInfo>());

            default:
                throw new InvalidOperationException($"Unsupported Backend packet manifest management operation '{request.Operation}'.");
        }
    }

    private async Task WriteDirectConnectCodeFailureAsync(
        MasterConnection connection,
        Guid requestId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.DirectConnectCodeResponse,
            DirectConnectCodeResponse.Failure(requestId, errorMessage),
            DirectConnectCodeResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDirectConnectCodeValidationFailureAsync(
        MasterConnection connection,
        Guid requestId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await connection.WriteControlAsync(
            MasterControlPacketIds.DirectConnectCodeValidationResponse,
            DirectConnectCodeValidationResponse.Failure(requestId, errorMessage),
            DirectConnectCodeValidationResponse.Codec,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExpireAdminStatusRouteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            m_AdminStatusRequestRoutes.TryRemove(requestId, out _);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool TryParseConnectionId(string value, out Guid connectionId)
    {
        return Guid.TryParseExact(value, "N", out connectionId) ||
               Guid.TryParse(value, out connectionId);
    }

    private static bool IsDirectConnectTargetNodeKind(MasterNodeKind nodeKind)
    {
        return nodeKind is MasterNodeKind.Dedicated or MasterNodeKind.Backend;
    }

    private static bool IsGatewayBackendRoutePolicyMutation(GatewayBackendRoutePolicyOperation operation)
    {
        return operation is
            GatewayBackendRoutePolicyOperation.Create or
            GatewayBackendRoutePolicyOperation.Update or
            GatewayBackendRoutePolicyOperation.Remove;
    }

    private static bool IsGatewayClientSecretCredentialMutation(GatewayClientSecretCredentialOperation operation)
    {
        return operation is
            GatewayClientSecretCredentialOperation.Create or
            GatewayClientSecretCredentialOperation.Update or
            GatewayClientSecretCredentialOperation.RotateSecret or
            GatewayClientSecretCredentialOperation.Remove;
    }

    private static bool IsBackendPacketManifestMutation(BackendPacketManifestOperation operation)
    {
        return operation is
            BackendPacketManifestOperation.Create or
            BackendPacketManifestOperation.Update or
            BackendPacketManifestOperation.Deprecate or
            BackendPacketManifestOperation.Remove;
    }

    private MasterOverviewSnapshot CreateOverviewSnapshot()
    {
        return new MasterOverviewSnapshot(
            GetSocketEndpoint(),
            [.. GetConnectionSnapshots()],
            DateTimeOffset.UtcNow);
    }

    private DedicatedNodeSnapshot CreateDedicatedNodeSnapshot()
    {
        return new DedicatedNodeSnapshot(
            [.. m_Connections.Values
                .Select(static connection => connection.TryCreateDedicatedNodeEndpoint())
                .Where(static endpoint => endpoint != null)
                .Select(static endpoint => endpoint!)
                .OrderBy(static endpoint => endpoint.NodeId, StringComparer.Ordinal)],
            DateTimeOffset.UtcNow);
    }

    private BackendNodeSnapshot CreateBackendNodeSnapshot()
    {
        return new BackendNodeSnapshot(
            [.. m_Connections.Values
                .Select(static connection => connection.TryCreateBackendNodeEndpoint())
                .Where(static endpoint => endpoint != null)
                .Select(static endpoint => endpoint!)
                .OrderBy(static endpoint => endpoint.BackendKind, StringComparer.Ordinal)
                .ThenBy(static endpoint => endpoint.NodeId, StringComparer.Ordinal)],
            DateTimeOffset.UtcNow);
    }

    private async Task<GatewayBackendRoutePolicySnapshot> CreateGatewayBackendRoutePolicySnapshotAsync(CancellationToken cancellationToken)
    {
        var allowedBackendKinds = await m_GatewayBackendRoutePolicy
            .GetAllowedBackendKindsAsync(cancellationToken)
            .ConfigureAwait(false);
        return new GatewayBackendRoutePolicySnapshot(
            allowedBackendKinds,
            DateTimeOffset.UtcNow);
    }

    private async Task<GatewayClientSecretCredentialSnapshot> CreateGatewayClientSecretCredentialSnapshotAsync(CancellationToken cancellationToken)
    {
        var secrets = await m_GatewayClientSecretCredentials
            .GetActiveSecretsAsync(cancellationToken)
            .ConfigureAwait(false);
        return new GatewayClientSecretCredentialSnapshot(
            secrets,
            DateTimeOffset.UtcNow);
    }

    private async Task<BackendPacketManifestSnapshot> CreateBackendPacketManifestSnapshotAsync(CancellationToken cancellationToken)
    {
        var manifests = await m_BackendPacketManifestStore
            .GetGatewayManifestsAsync(cancellationToken)
            .ConfigureAwait(false);
        return new BackendPacketManifestSnapshot(
            manifests,
            DateTimeOffset.UtcNow);
    }

    private async Task AuthenticateNodeAsync(MasterConnection connection, Stream stream, CancellationToken cancellationToken)
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

        if (hello.ProtocolVersion != MasterControlProtocol.SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported node protocol version {hello.ProtocolVersion}.");
        }

        using var proofFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.NodeAuthProof,
            handshakeTimeout.Token).ConfigureAwait(false);
        var proof = PacketCodec.Decode(proofFrame, NodeAuthProof.Codec);

        var credential = await nodeAuthSecretProvider.GetSharedSecretAsync(
            hello.NodeKind,
            hello.NodeId,
            handshakeTimeout.Token).ConfigureAwait(false);
        if (credential == null ||
            !MasterNodeAuthenticator.VerifyProof(challenge, hello, proof, credential.SharedSecret))
        {
            throw new UnauthorizedAccessException($"Node authentication proof was rejected for node '{hello.NodeId}'.");
        }

        var accepted = new NodeAccepted(hello.NodeId, connection.ConnectionId.ToString("N"));
        using (var acceptedFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   MasterControlPacketIds.NodeAccepted,
                   MasterControlProtocol.SchemaVersion,
                   accepted,
                   NodeAccepted.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, acceptedFrame, handshakeTimeout.Token).ConfigureAwait(false);
        }

        connection.MarkAccepted(
            hello.NodeKind,
            hello.NodeId,
            hello.DisplayName,
            credential.CredentialVersion,
            credential.AuthorizedBackendKind);
        NotifyConnectionsChanged();
        logger.LogInformation(
            "Master node accepted. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}.",
            connection.ConnectionId,
            hello.NodeKind,
            hello.NodeId);
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
            throw new EndOfStreamException("Node connection closed before Master handshake completed.");
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

    private void LogNodeDisconnected(MasterConnection connection)
    {
        if (connection.IsTrusted)
        {
            logger.LogInformation(
                "Master node disconnected. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connection.ConnectionId,
                connection.NodeKind,
                connection.NodeId,
                connection.RemoteEndPoint);
            return;
        }

        logger.LogInformation(
            "Unauthenticated Master node connection closed. ConnectionId={ConnectionId}, RemoteEndPoint={RemoteEndPoint}.",
            connection.ConnectionId,
            connection.RemoteEndPoint);
    }

    private void LogNodeDisconnectedAbruptly(MasterConnection connection, Exception exception)
    {
        if (connection.IsTrusted)
        {
            logger.LogWarning(
                "Master node disconnected abruptly. The remote process likely stopped or reset the socket. ConnectionId={ConnectionId}, NodeKind={NodeKind}, NodeId={NodeId}, RemoteEndPoint={RemoteEndPoint}.",
                connection.ConnectionId,
                connection.NodeKind,
                connection.NodeId,
                connection.RemoteEndPoint);
        }
        else
        {
            logger.LogWarning(
                "Unauthenticated Master node connection was reset before handshake completed. RemoteEndPoint={RemoteEndPoint}.",
                connection.RemoteEndPoint);
        }

        logger.LogDebug(exception, "Remote disconnect details.");
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

    private void NotifyConnectionsChanged()
    {
        var handlers = ConnectionsChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Master connection change subscriber failed.");
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

    private static async Task<X509Certificate2> LoadCertificateAsync(MasterSocketOptions options, CancellationToken cancellationToken)
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

    private sealed class MasterConnection(Socket socket) : IDisposable
    {
        private long m_LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private int m_Trusted;
        private readonly SemaphoreSlim m_WriteLock = new(1, 1);
        private Stream? m_Stream;

        public Guid ConnectionId { get; } = Guid.NewGuid();

        public Socket Socket { get; } = socket;

        public MasterNodeKind NodeKind { get; private set; } = MasterNodeKind.Unknown;

        public string NodeId { get; private set; } = string.Empty;

        public string DisplayName { get; private set; } = string.Empty;

        public string CredentialVersion { get; private set; } = string.Empty;

        public string? AuthorizedBackendKind { get; private set; }

        public string RemoteEndPoint { get; } = socket.RemoteEndPoint?.ToString() ?? "unknown";

        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

        public bool IsTrusted => Volatile.Read(ref m_Trusted) == 1;

        public MasterSocketEndpoint? DedicatedGatewayEndpoint { get; private set; }

        public DateTimeOffset? DedicatedGatewayEndpointAdvertisedAt { get; private set; }

        public MasterSocketEndpoint? BackendGatewayEndpoint { get; private set; }

        public string BackendKind { get; private set; } = string.Empty;

        public BackendPacketManifestId? BackendManifestId { get; private set; }

        public BackendPacketManifestHash? BackendManifestHash { get; private set; }

        public BackendServerDescriptor? BackendDescriptor { get; private set; }

        public GatewayAuthenticationMethodDefinition[] BackendAuthenticationMethods { get; private set; } = [];

        public DateTimeOffset? BackendGatewayEndpointAdvertisedAt { get; private set; }

        public void AttachStream(Stream stream)
        {
            m_Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public void MarkAccepted(
            MasterNodeKind nodeKind,
            string nodeId,
            string displayName,
            string credentialVersion,
            string? authorizedBackendKind)
        {
            NodeKind = nodeKind;
            NodeId = nodeId;
            DisplayName = displayName;
            CredentialVersion = credentialVersion;
            AuthorizedBackendKind = authorizedBackendKind;
            MarkSeen();
            Volatile.Write(ref m_Trusted, 1);
        }

        public void UpdateDedicatedGatewayEndpoint(MasterSocketEndpoint endpoint)
        {
            DedicatedGatewayEndpoint = endpoint;
            DedicatedGatewayEndpointAdvertisedAt = DateTimeOffset.UtcNow;
            MarkSeen();
        }

        public void UpdateBackendGatewayEndpoint(
            string backendKind,
            MasterSocketEndpoint endpoint,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash,
            BackendServerDescriptor descriptor,
            GatewayAuthenticationMethodDefinition[] authenticationMethods)
        {
            BackendKind = backendKind;
            BackendGatewayEndpoint = endpoint;
            BackendManifestId = manifestId;
            BackendManifestHash = manifestHash;
            BackendDescriptor = descriptor;
            BackendAuthenticationMethods = authenticationMethods;
            BackendGatewayEndpointAdvertisedAt = DateTimeOffset.UtcNow;
            MarkSeen();
        }

        public void MarkSeen()
        {
            Interlocked.Exchange(ref m_LastSeenAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public MasterConnectionSnapshot ToSnapshot()
        {
            var isTrusted = IsTrusted;
            var nodeKind = isTrusted ? NodeKind : MasterNodeKind.Unknown;
            var backendKind = isTrusted && BackendNodeEndpoint.IsBackendNodeKind(NodeKind)
                ? GetSnapshotBackendKind()
                : string.Empty;

            return new MasterConnectionSnapshot(
                ConnectionId,
                RemoteEndPoint,
                nodeKind,
                isTrusted ? NodeId : string.Empty,
                isTrusted ? DisplayName : string.Empty,
                backendKind,
                ConnectedAt,
                DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref m_LastSeenAt)));
        }

        private string GetSnapshotBackendKind()
        {
            return !string.IsNullOrWhiteSpace(AuthorizedBackendKind)
                ? AuthorizedBackendKind
                : BackendKind;
        }

        public DedicatedNodeEndpoint? TryCreateDedicatedNodeEndpoint()
        {
            var endpoint = DedicatedGatewayEndpoint;
            var advertisedAt = DedicatedGatewayEndpointAdvertisedAt;
            if (!IsTrusted ||
                NodeKind != MasterNodeKind.Dedicated ||
                endpoint == null ||
                !advertisedAt.HasValue)
            {
                return null;
            }

            return new DedicatedNodeEndpoint(
                NodeId,
                DisplayName,
                ConnectionId.ToString("N"),
                endpoint,
                advertisedAt.Value);
        }

        public BackendNodeEndpoint? TryCreateBackendNodeEndpoint()
        {
            var endpoint = BackendGatewayEndpoint;
            var advertisedAt = BackendGatewayEndpointAdvertisedAt;
            if (!IsTrusted ||
                !BackendNodeEndpoint.IsBackendNodeKind(NodeKind) ||
                string.IsNullOrWhiteSpace(BackendKind) ||
                endpoint == null ||
                !BackendManifestId.HasValue ||
                !BackendManifestHash.HasValue ||
                BackendDescriptor == null ||
                !advertisedAt.HasValue)
            {
                return null;
            }

            return new BackendNodeEndpoint(
                BackendKind,
                NodeId,
                DisplayName,
                ConnectionId.ToString("N"),
                endpoint,
                BackendManifestId.Value,
                BackendManifestHash.Value,
                BackendDescriptor,
                BackendAuthenticationMethods,
                advertisedAt.Value);
        }

        public async Task WriteControlAsync<TPacket>(
            ushort packetId,
            TPacket value,
            IPacketCodec<TPacket> codec,
            CancellationToken cancellationToken)
        {
            var stream = m_Stream ?? throw new InvalidOperationException("Connection stream is not attached.");
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                packetId,
                MasterControlProtocol.SchemaVersion,
                value,
                codec);

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

        public void Dispose()
        {
            Socket.Dispose();
        }
    }
}
