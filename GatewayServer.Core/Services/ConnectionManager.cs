using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GatewayServer.Behaviors;
using GatewayServer.Options;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal interface IBackendRouteStatusProvider
{
    ServiceAdminStatusItem[] GetStatusItems();
}

internal class ConnectionManager(
    IOptions<ConnectionManagerOptions> options,
    IOptions<BackendRouteOptions> backendRouteOptions,
    IBackendRouteManager backendRouteManager,
    IGatewayBackendRoutePolicyProvider gatewayBackendRoutePolicy,
    IBackendPacketManifestProvider backendPacketManifests,
    IGatewayClientCertificateProvider certificateProvider,
    IGatewayClientStreamAuthenticator streamAuthenticator,
    IGatewayClientAuthenticationContextFactory authenticationContextFactory,
    IGatewayClientAuthenticationChallengeIssuer authenticationChallengeIssuer,
    IGatewayClientTokenValidator clientTokenValidator,
    IGatewayBackendRouteTokenGenerator routeTokenGenerator,
    ILogger<ConnectionManager> logger) : IHostedService, IConnectionManager, IBackendRouteStatusProvider
{
    private readonly CancellationTokenSource m_GracefulCancellation = new();
    private readonly BackendRouteOptions m_BackendRouteOptions = backendRouteOptions.Value;
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_ClientOriginPrincipalExchangeCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_BackendOriginPrincipalExchangeCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Client, FixedWindowRateCounter> m_ServerListClientCounters = new();
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_ServerListPrincipalCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> m_PacketManifestRejectCounters = new(StringComparer.Ordinal);
    private readonly PersistentBackendRouteRegistry<Client> m_PersistentBackendRouteRegistry = new(
        backendRouteOptions.Value,
        gatewayBackendRoutePolicy,
        routeTokenGenerator,
        logger);

    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private readonly HashSet<Client> m_Clients = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionOptions = options.Value;
        if (!connectionOptions.UseTls)
        {
            throw new InvalidOperationException("Gateway client listener requires TLS. Development may use a private or self-signed certificate, but plaintext TCP is not supported.");
        }

        try
        {
            var listenAddress = await MasterEndpointResolver.ResolveBindAddressAsync(
                connectionOptions.IPAddress,
                cancellationToken).ConfigureAwait(false);
            m_Socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (listenAddress.Equals(IPAddress.IPv6Any))
            {
                m_Socket.DualMode = true;
            }

            m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            m_Socket.Bind(new IPEndPoint(listenAddress, connectionOptions.Port));
            m_Socket.Listen();
        }
        catch
        {
            m_Socket?.Dispose();
            m_Socket = null;
            throw;
        }

        backendRouteManager.RouteDataFrameReceived += OnBackendRouteDataFrameReceivedAsync;
        backendRouteManager.RouteCloseFrameReceived += OnBackendRouteCloseFrameReceivedAsync;
        backendRouteManager.RouteSessionClosed += OnBackendRouteSessionClosedAsync;
        logger.LogInformation("Gateway client listener is running on {Address}:{Port} with TLS.", connectionOptions.IPAddress, connectionOptions.Port);

        m_AcceptTask = StartAcceptAsync(m_GracefulCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        backendRouteManager.RouteDataFrameReceived -= OnBackendRouteDataFrameReceivedAsync;
        backendRouteManager.RouteCloseFrameReceived -= OnBackendRouteCloseFrameReceivedAsync;
        backendRouteManager.RouteSessionClosed -= OnBackendRouteSessionClosedAsync;
        await m_GracefulCancellation.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();
        m_PersistentBackendRouteRegistry.CancelAll();

        if (m_AcceptTask != null)
        {
            await WaitForShutdownAsync(m_AcceptTask, cancellationToken).ConfigureAwait(false);
        }

        await DisposeClientsAsync().ConfigureAwait(false);
    }

    private async Task StartAcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? clientSocket = null;

            try
            {
                clientSocket = await m_Socket!.AcceptAsync(cancellationToken).ConfigureAwait(false);
                StartHandshakeAsync(clientSocket, cancellationToken);
                clientSocket = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                clientSocket?.Dispose();
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                clientSocket?.Dispose();
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                clientSocket?.Dispose();
                return;
            }
            catch (Exception e)
            {
                clientSocket?.Dispose();
                logger.LogError(e, "Error occurred while accepting a Gateway client connection.");
            }
        }
    }

    private async void StartHandshakeAsync(Socket socket, CancellationToken cancellationToken)
    {
        socket.NoDelay = true;

        var networkStream = new NetworkStream(socket, ownsSocket: true);
        Stream? s = null;
        GatewayClientCertificateLease? certificateLease = null;

        try
        {
            certificateLease = certificateProvider.AcquireLease();
            s = await streamAuthenticator
                .AuthenticateAsync(networkStream, certificateLease.Certificate, cancellationToken)
                .ConfigureAwait(false);
            certificateLease.Dispose();
            certificateLease = null;

            var handshakeNotify = new GatewayHandshakeNotify("https://accounts.ayla.r-e.kr/authorize");
            using var handshakeFrame = PacketCodec.Encode(
                PacketKind.Notify,
                Pid.GATE_HANDSHAKE_NOTIFY,
                version: 1,
                handshakeNotify,
                GatewayHandshakeNotify.Codec);
            await PacketFrameWriter.WriteAsync(s, handshakeFrame, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            certificateLease?.Dispose();
            await DisposeHandshakeStreamsAsync(networkStream, s).ConfigureAwait(false);
            socket.Dispose();

            return;
        }
        catch (Exception e)
        {
            certificateLease?.Dispose();
            logger.LogError("Error during handshake: {Message}", e.Message);

            await DisposeHandshakeStreamsAsync(networkStream, s).ConfigureAwait(false);
            socket.Dispose();

            return;
        }

        var client = new Client(
            networkStream,
            s,
            logger,
            options.Value.MaxQueuedClientPackets,
            options.Value.ClientIdleTimeoutMilliseconds);
        GatewayClientAuthenticationContext authenticationContext;
        try
        {
            authenticationContext = authenticationContextFactory.Create(client);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Gateway could not create authentication state for a client connection.");
            await client.DisposeAsync().ConfigureAwait(false);
            return;
        }

        bool addedToClients = false;
        lock (m_Clients)
        {
            addedToClients = m_Clients.Add(client);
            if (!addedToClients)
            {
                logger.LogError("Failed to add client to the set. This should never happen since Client does not override GetHashCode or Equals.");
            }
            else
            {
                client.Completed += () =>
                {
                    lock (m_Clients)
                    {
                        bool removed = m_Clients.Remove(client);
                        Debug.Assert(removed);
                    }

                    RemoveBackendRoutes(client);
                };

                StartAuthenticationTimeout(
                    client,
                    authenticationContext,
                    options.Value.ClientAuthenticationTimeoutMilliseconds,
                    cancellationToken);
                client.Start();
                HandleClientPacketsAsync(client, authenticationContext, cancellationToken);
            }
        }

        if (!addedToClients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await networkStream.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();
        }
    }

    private async void HandleClientPacketsAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in client.ReadPacketsAsync(cancellationToken))
            {
                if (packet.Header.PacketId == Pid.GATE_CLIENT_AUTHENTICATE)
                {
                    await HandleClientAuthenticatePacketAsync(
                        client,
                        authenticationContext,
                        packet,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (packet.Header.PacketId == Pid.GATE_CLIENT_AUTHENTICATION_OPTIONS)
                {
                    await HandleClientAuthenticationOptionsPacketAsync(
                        client,
                        packet,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (IsBackendRoutePacket(packet))
                {
                    await HandleBackendRouteClientPacketAsync(
                        client,
                        authenticationContext,
                        packet,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (packet.Header.Kind == PacketKind.Response)
                {
                    logger.LogWarning(
                        "Gateway rejected unsupported client response packet. PacketId={PacketId}, Version={Version}.",
                        packet.Header.PacketId,
                        packet.Header.Version);
                    continue;
                }

                await EchoPacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error occurred while echoing packets.");
        }
        finally
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Gateway client disposal completed with an error after echo loop.");
            }
        }
    }

    private async Task HandleClientAuthenticationOptionsPacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        var backendKind = string.Empty;

        try
        {
            if (packet.Header.Kind != PacketKind.Request)
            {
                throw new InvalidOperationException("Gateway client authentication options packets must be Request packets.");
            }

            if (packet.Header.Version != GatewayClientAuthenticationOptionsRequest.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Gateway client authentication options protocol version {packet.Header.Version}.");
            }

            var request = PacketCodec.Decode(packet, GatewayClientAuthenticationOptionsRequest.Codec);
            backendKind = request.BackendKind;
            var normalizedBackendKind = m_PersistentBackendRouteRegistry.RequireAllowedBackendKind(request.BackendKind);
            var methods = backendRouteManager.GetAuthenticationMethods(normalizedBackendKind, request.ServerHandle);
            if (methods.Length == 0)
            {
                throw new InvalidOperationException("No Gateway authentication methods are available for the requested Backend server.");
            }

            var challenges = await authenticationChallengeIssuer
                .CreateChallengesAsync(methods, normalizedBackendKind, request.ServerHandle, cancellationToken)
                .ConfigureAwait(false);
            if (challenges.Length == 0)
            {
                throw new InvalidOperationException("No Gateway authentication challenges are available for the requested Backend server.");
            }

            await WriteClientAuthenticationOptionsResponseAsync(
                client,
                packet.Header.Version,
                GatewayClientAuthenticationOptionsResponse.Accepted(normalizedBackendKind, challenges),
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
                "Gateway rejected client authentication options packet. BackendKind={BackendKind}, PacketKind={PacketKind}, PacketId={PacketId}.",
                backendKind,
                packet.Header.Kind,
                packet.Header.PacketId);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteClientAuthenticationOptionsResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayClientAuthenticationOptionsResponse.Rejected(GetResponseBackendKind(backendKind), "Gateway client authentication options were rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAuthenticatePacketAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        try
        {
            if (packet.Header.Kind != PacketKind.Request)
            {
                throw new InvalidOperationException("Gateway client authentication packets must be Request packets.");
            }

            if (packet.Header.Version != GatewayClientAuthenticateRequest.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Gateway client authentication protocol version {packet.Header.Version}.");
            }

            if (authenticationContext.IsAuthenticated)
            {
                var currentPrincipal = authenticationContext.Principal ?? throw new InvalidOperationException("Authenticated Gateway client context is missing a principal.");
                await WriteClientAuthenticateResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayClientAuthenticateResponse.Accepted(currentPrincipal.SubjectId),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var request = PacketCodec.Decode(packet, GatewayClientAuthenticateRequest.Codec);
            var result = await clientTokenValidator
                .ValidateAsync(request.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            if (result.Success)
            {
                var principal = result.Principal ?? throw new InvalidOperationException("Gateway client token validation succeeded without a principal.");
                authenticationContext.MarkAuthenticated(principal);
                await WriteClientAuthenticateResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayClientAuthenticateResponse.Accepted(principal.SubjectId),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteClientAuthenticateResponseAsync(
                client,
                packet.Header.Version,
                GatewayClientAuthenticateResponse.Rejected(GetAuthenticationErrorMessage(result.ErrorMessage)),
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
                "Gateway rejected client authentication packet. PacketKind={PacketKind}, PacketId={PacketId}.",
                packet.Header.Kind,
                packet.Header.PacketId);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteClientAuthenticateResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayClientAuthenticateResponse.Rejected("Gateway client authentication was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void StartAuthenticationTimeout(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (timeoutMilliseconds <= 0 ||
            authenticationContext.IsAuthenticated)
        {
            return;
        }

        var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        client.Completed += () =>
        {
            _ = timeoutCancellation.CancelAsync();
        };
        _ = MonitorAuthenticationTimeoutAsync(
            client,
            authenticationContext,
            timeoutMilliseconds,
            timeoutCancellation);
    }

    private async Task MonitorAuthenticationTimeoutAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        int timeoutMilliseconds,
        CancellationTokenSource timeoutCancellation)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1, timeoutMilliseconds)),
                timeoutCancellation.Token).ConfigureAwait(false);
            if (!authenticationContext.IsAuthenticated)
            {
                logger.LogWarning("Gateway client authentication timed out.");
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Gateway client authentication timeout monitor failed.");
        }
        finally
        {
            timeoutCancellation.Dispose();
        }
    }

    private async Task HandleBackendRouteClientPacketAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        if (!authenticationContext.IsAuthenticated)
        {
            await RejectUnauthenticatedBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE)
        {
            await RejectRemovedLegacyBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_OPEN)
        {
            await HandleBackendRouteOpenPacketAsync(client, authenticationContext, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_SERVER_LIST)
        {
            await HandleBackendServerListPacketAsync(client, authenticationContext, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_DATA)
        {
            await HandleBackendRouteDataPacketAsync(client, authenticationContext, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_CLOSE)
        {
            await HandleBackendRouteClosePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RejectUnsupportedPersistentBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleBackendRouteOpenPacketAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        PersistentBackendRoute<Client>? openedRoute = null;
        var backendKind = string.Empty;
        var backendOpenNotified = false;

        try
        {
            if (packet.Header.Kind != PacketKind.Request)
            {
                throw new InvalidOperationException("Backend route open packets must be Request packets.");
            }

            var request = DecodeBackendRouteOpenRequest(packet);
            backendKind = request.BackendKind;
            var principalSubjectId = authenticationContext.Principal?.SubjectId;
            m_PersistentBackendRouteRegistry.RequireOpenAttemptAllowed(client, principalSubjectId);
            var normalizedBackendKind = m_PersistentBackendRouteRegistry.RequireAllowedBackendKind(request.BackendKind);
            RequireAuthenticationMethodAllowed(
                authenticationContext,
                normalizedBackendKind,
                request.ServerHandle);

            var backendSession = request.ServerHandle == null
                ? await backendRouteManager
                    .ConnectAsync(normalizedBackendKind, cancellationToken)
                    .ConfigureAwait(false)
                : await backendRouteManager
                    .ConnectAsync(normalizedBackendKind, request.ServerHandle, cancellationToken)
                    .ConfigureAwait(false);
            if (!string.Equals(backendSession.BackendKind, normalizedBackendKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Selected Backend session kind does not match the requested Backend kind.");
            }

            RequireAuthenticationMethodAllowed(
                authenticationContext,
                normalizedBackendKind,
                backendSession.ServerHandle);

            backendPacketManifests.RequireManifest(
                backendSession.BackendKind,
                backendSession.ManifestId,
                backendSession.ManifestHash);

            var route = m_PersistentBackendRouteRegistry.Open(
                backendSession.Binding,
                client,
                principalSubjectId,
                backendSession.ManifestId,
                backendSession.ManifestHash,
                m_GracefulCancellation.Token);
            openedRoute = route;

            await NotifyBackendRouteOpenedAsync(route, cancellationToken).ConfigureAwait(false);
            backendOpenNotified = true;

            await WriteBackendRouteOpenResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendRouteOpenResponse.Accepted(
                    route.RouteToken,
                    route.BackendKind,
                    backendSession.ServerHandle,
                    backendSession.DescriptorVersion,
                    backendSession.DescriptorHash),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            if (openedRoute != null &&
                openedRoute.State == PersistentBackendRouteState.Open)
            {
                m_PersistentBackendRouteRegistry.Close(
                    openedRoute.RouteToken,
                    "Backend route open failed.");
                if (backendOpenNotified)
                {
                    await NotifyBackendRouteClosedAsync(
                        openedRoute,
                        "Backend route open failed.",
                        cancellationToken).ConfigureAwait(false);
                }
            }

            logger.LogWarning(
                e,
                "Gateway rejected Backend route open packet. BackendKind={BackendKind}, PacketKind={PacketKind}, PacketId={PacketId}.",
                backendKind,
                packet.Header.Kind,
                packet.Header.PacketId);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteBackendRouteOpenResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteOpenResponse.Rejected(GetResponseBackendKind(backendKind), "Backend route open was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleBackendServerListPacketAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        var backendKind = string.Empty;

        try
        {
            if (packet.Header.Kind != PacketKind.Request)
            {
                throw new InvalidOperationException("Backend server list packets must be Request packets.");
            }

            if (packet.Header.Version != GatewayBackendServerListRequest.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Backend server list protocol version {packet.Header.Version}.");
            }

            var request = PacketCodec.Decode(packet, GatewayBackendServerListRequest.Codec);
            backendKind = request.BackendKind;
            var principalSubjectId = authenticationContext.Principal?.SubjectId;
            RequireServerListAllowed(client, principalSubjectId);
            var normalizedBackendKind = m_PersistentBackendRouteRegistry.RequireAllowedBackendKind(request.BackendKind);
            var snapshot = backendRouteManager.ListServers(
                normalizedBackendKind,
                GetServerListEntryLimit(request));

            await WriteBackendServerListResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendServerListResponse.Accepted(
                    normalizedBackendKind,
                    snapshot.Entries,
                    snapshot.ObservedAt),
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
                "Gateway rejected Backend server list packet. BackendKind={BackendKind}, PacketKind={PacketKind}, PacketId={PacketId}.",
                backendKind,
                packet.Header.Kind,
                packet.Header.PacketId);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteBackendServerListResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendServerListResponse.Rejected(GetResponseBackendKind(backendKind), "Backend server list was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleBackendRouteClosePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        GatewayBackendRouteClose? request = null;

        try
        {
            if (packet.Header.Kind != PacketKind.Request)
            {
                throw new InvalidOperationException("Backend route close packets must be Request packets.");
            }

            if (packet.Header.Version != GatewayBackendRouteClose.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Backend route close protocol version {packet.Header.Version}.");
            }

            request = PacketCodec.Decode(packet, GatewayBackendRouteClose.Codec);
            if (!m_PersistentBackendRouteRegistry.TryGet(request.RouteToken, out var route))
            {
                throw new InvalidOperationException("Unknown Backend route token.");
            }

            if (!ReferenceEquals(route.Owner, client))
            {
                throw new UnauthorizedAccessException("Backend route token is not owned by this client.");
            }

            if (route.State != PersistentBackendRouteState.Open)
            {
                throw new InvalidOperationException("Persistent Backend route is not open.");
            }

            if (!m_PersistentBackendRouteRegistry.Close(request.RouteToken, request.Reason))
            {
                throw new InvalidOperationException("Persistent Backend route could not be closed.");
            }

            await WriteBackendRouteCloseResponseAsync(
                client,
                packet.Header.Version,
                request,
                cancellationToken).ConfigureAwait(false);
            await NotifyBackendRouteClosedAsync(route, request.Reason, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Gateway rejected Backend route close packet. PacketKind={PacketKind}, PacketId={PacketId}.",
                packet.Header.Kind,
                packet.Header.PacketId);
        }
    }

    private async Task HandleBackendRouteDataPacketAsync(
        Client client,
        GatewayClientAuthenticationContext authenticationContext,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        PersistentBackendRoute<Client>? route = null;
        GatewayBackendExchangeId? registeredExchangeId = null;
        GatewayBackendExchangeId? matchedBackendOriginResponseExchangeId = null;
        var exchangeRegistered = false;
        var backendKind = string.Empty;

        try
        {
            if (packet.Header.Kind is not (PacketKind.Request or PacketKind.Response or PacketKind.Notify))
            {
                throw new InvalidOperationException("Client Backend route data packets must be Request, Response, or Notify packets.");
            }

            if (packet.Header.Version != GatewayBackendRouteDataEnvelope.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Backend route data protocol version {packet.Header.Version}.");
            }

            var envelope = PacketCodec.Decode(packet, GatewayBackendRouteDataEnvelope.Codec);
            if (envelope.Direction != GatewayBackendRouteDirection.ClientToBackend)
            {
                throw new InvalidOperationException("Client Backend route data packets must use the ClientToBackend direction.");
            }

            if (packet.Header.Kind != envelope.RoutedKind)
            {
                throw new InvalidOperationException("Backend route data packet kind must match the routed packet kind.");
            }

            if (!m_PersistentBackendRouteRegistry.TryGet(envelope.RouteToken, out route))
            {
                throw new InvalidOperationException("Unknown Backend route token.");
            }

            if (!ReferenceEquals(route.Owner, client))
            {
                throw new UnauthorizedAccessException("Backend route token is not owned by this client.");
            }

            if (route.State != PersistentBackendRouteState.Open)
            {
                throw new InvalidOperationException("Persistent Backend route is not open.");
            }

            backendKind = route.BackendKind;
            RequirePacketManifestMatch(
                route,
                BackendPacketManifestDirection.ClientToBackend,
                envelope.RoutedKind,
                envelope.RoutedPacketId,
                envelope.RoutedVersion,
                envelope.RoutedPayload);

            if (envelope.RoutedKind == PacketKind.Request)
            {
                registeredExchangeId = envelope.ExchangeId ?? throw new InvalidOperationException("Client Backend route requests require an exchange id.");
                RequirePrincipalExchangeCreationAllowed(
                    route.PrincipalSubjectId ?? authenticationContext.Principal?.SubjectId,
                    m_BackendRouteOptions.MaxClientOriginExchangeCreatesPerPrincipalPerWindow,
                    m_ClientOriginPrincipalExchangeCounters,
                    "client-origin");
                route.RegisterClientOriginExchange(
                    registeredExchangeId.Value,
                    envelope.RoutedPacketId,
                    envelope.RoutedVersion);
                exchangeRegistered = true;
            }
            else if (envelope.RoutedKind == PacketKind.Response)
            {
                var exchangeId = envelope.ExchangeId ?? throw new InvalidOperationException("Client Backend route responses require an exchange id.");
                if (!route.ContainsBackendOriginExchange(exchangeId))
                {
                    throw new InvalidOperationException("Client Backend route response did not match a pending Backend-origin exchange.");
                }

                matchedBackendOriginResponseExchangeId = exchangeId;
            }

            var channelEnvelope = new GatewayBackendChannelDataEnvelope(
                route.ChannelId,
                envelope.RoutedKind,
                envelope.RoutedPacketId,
                envelope.RoutedVersion,
                envelope.ExchangeId,
                envelope.RoutedPayload);

            using var routedFrame = PacketCodec.Encode(
                envelope.RoutedKind,
                Pid.GATE_BACKEND_CHANNEL_DATA,
                GatewayBackendChannelDataEnvelope.ProtocolVersion,
                channelEnvelope,
                GatewayBackendChannelDataEnvelope.Codec);
            await backendRouteManager.RelayFrameAsync(
                route.BackendBinding,
                routedFrame,
                cancellationToken).ConfigureAwait(false);
            if (matchedBackendOriginResponseExchangeId.HasValue)
            {
                route.RemoveBackendOriginExchange(matchedBackendOriginResponseExchangeId.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            if (exchangeRegistered &&
                route != null &&
                registeredExchangeId.HasValue)
            {
                route.RemoveClientOriginExchange(registeredExchangeId.Value);
            }

            logger.LogWarning(
                e,
                "Gateway rejected Backend route data packet. BackendKind={BackendKind}, PacketKind={PacketKind}, PacketId={PacketId}.",
                backendKind,
                packet.Header.Kind,
                packet.Header.PacketId);
        }
    }

    private async Task RejectUnauthenticatedBackendRoutePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Gateway rejected Backend route packet from an unauthenticated client. PacketKind={PacketKind}, PacketId={PacketId}.",
            packet.Header.Kind,
            packet.Header.PacketId);

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE &&
            packet.Header.Kind == PacketKind.Request)
        {
            var (routeId, backendKind) = GetBackendRouteResponseIdentity(packet);
            await WriteBackendRouteResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendRouteResponse.Rejected(routeId, backendKind, "Client is not authenticated."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_OPEN &&
            packet.Header.Kind == PacketKind.Request)
        {
            var backendKind = GetBackendRouteOpenResponseBackendKind(packet);
            await WriteBackendRouteOpenResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendRouteOpenResponse.Rejected(backendKind, "Client is not authenticated."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_SERVER_LIST &&
            packet.Header.Kind == PacketKind.Request)
        {
            var backendKind = GetBackendServerListResponseBackendKind(packet);
            await WriteBackendServerListResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendServerListResponse.Rejected(backendKind, "Client is not authenticated."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RejectRemovedLegacyBackendRoutePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Gateway rejected removed legacy Backend route packet. PacketKind={PacketKind}, PacketId={PacketId}.",
            packet.Header.Kind,
            packet.Header.PacketId);

        if (packet.Header.Kind != PacketKind.Request)
        {
            return;
        }

        var (routeId, backendKind) = GetBackendRouteResponseIdentity(packet);
        await WriteBackendRouteResponseAsync(
            client,
            packet.Header.Version,
            GatewayBackendRouteResponse.Rejected(routeId, backendKind, "Legacy Backend route flow has been removed."),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RejectUnsupportedPersistentBackendRoutePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Gateway rejected unsupported persistent Backend route packet. PacketKind={PacketKind}, PacketId={PacketId}.",
            packet.Header.Kind,
            packet.Header.PacketId);

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_OPEN &&
            packet.Header.Kind == PacketKind.Request)
        {
            var backendKind = GetBackendRouteOpenResponseBackendKind(packet);
            await WriteBackendRouteOpenResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendRouteOpenResponse.Rejected(backendKind, "Persistent Backend route handling is not enabled."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask OnBackendRouteDataFrameReceivedAsync(
        BackendRouteDataFrameReceived frame,
        CancellationToken cancellationToken)
    {
        PersistentBackendRoute<Client>? route = null;
        GatewayBackendExchangeId? registeredExchangeId = null;
        GatewayBackendExchangeId? matchedClientOriginResponseExchangeId = null;
        var exchangeRegistered = false;

        try
        {
            var envelope = frame.Envelope;
            if (!m_PersistentBackendRouteRegistry.TryGet(envelope.ChannelId, out route))
            {
                logger.LogWarning(
                    "Gateway received Backend route data for an unknown channel. BackendKind={BackendKind}, ChannelId={ChannelId}.",
                    frame.BackendKind,
                    envelope.ChannelId);
                return;
            }

            if (!route.BackendBinding.Matches(
                    frame.BackendKind,
                    frame.NodeId,
                    frame.MasterConnectionId,
                    frame.DirectConnectionId))
            {
                throw new InvalidOperationException("Backend route data used a Backend session that does not match the route binding.");
            }

            if (route.State != PersistentBackendRouteState.Open)
            {
                throw new InvalidOperationException("Persistent Backend route is not open.");
            }

            RequirePacketManifestMatch(
                route,
                BackendPacketManifestDirection.BackendToClient,
                envelope.RoutedKind,
                envelope.RoutedPacketId,
                envelope.RoutedVersion,
                envelope.RoutedPayload);

            if (envelope.RoutedKind == PacketKind.Request)
            {
                registeredExchangeId = envelope.ExchangeId ?? throw new InvalidOperationException("Backend route requests require an exchange id.");
                RequirePrincipalExchangeCreationAllowed(
                    route.PrincipalSubjectId,
                    m_BackendRouteOptions.MaxBackendOriginExchangeCreatesPerPrincipalPerWindow,
                    m_BackendOriginPrincipalExchangeCounters,
                    "Backend-origin");
                route.RegisterBackendOriginExchange(
                    registeredExchangeId.Value,
                    envelope.RoutedPacketId,
                    envelope.RoutedVersion);
                exchangeRegistered = true;
            }
            else if (envelope.RoutedKind == PacketKind.Response)
            {
                var exchangeId = envelope.ExchangeId ?? throw new InvalidOperationException("Backend route responses require an exchange id.");
                if (!route.ContainsClientOriginExchange(exchangeId))
                {
                    throw new InvalidOperationException("Backend route response did not match a pending client-origin exchange.");
                }

                matchedClientOriginResponseExchangeId = exchangeId;
            }

            var clientEnvelope = new GatewayBackendRouteDataEnvelope(
                route.RouteToken,
                GatewayBackendRouteDirection.BackendToClient,
                envelope.RoutedKind,
                envelope.RoutedPacketId,
                envelope.RoutedVersion,
                envelope.ExchangeId,
                envelope.RoutedPayload);

            using var clientFrame = PacketCodec.Encode(
                envelope.RoutedKind,
                Pid.GATE_BACKEND_ROUTE_DATA,
                GatewayBackendRouteDataEnvelope.ProtocolVersion,
                clientEnvelope,
                GatewayBackendRouteDataEnvelope.Codec);
            await route.Owner.WriteAsync(clientFrame, cancellationToken).ConfigureAwait(false);
            if (matchedClientOriginResponseExchangeId.HasValue)
            {
                route.RemoveClientOriginExchange(matchedClientOriginResponseExchangeId.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            if (exchangeRegistered &&
                route != null &&
                registeredExchangeId.HasValue)
            {
                route.RemoveBackendOriginExchange(registeredExchangeId.Value);
            }

            logger.LogWarning(
                e,
                "Gateway rejected Backend route data frame. BackendKind={BackendKind}, NodeId={NodeId}.",
                frame.BackendKind,
                frame.NodeId);
        }
    }

    private async ValueTask OnBackendRouteCloseFrameReceivedAsync(
        BackendRouteCloseFrameReceived frame,
        CancellationToken cancellationToken)
    {
        PersistentBackendRoute<Client>? route = null;

        try
        {
            var close = frame.Close;
            if (!m_PersistentBackendRouteRegistry.TryGet(close.ChannelId, out route))
            {
                logger.LogWarning(
                    "Gateway received Backend route close for an unknown channel. BackendKind={BackendKind}, NodeId={NodeId}, ChannelId={ChannelId}.",
                    frame.BackendKind,
                    frame.NodeId,
                    close.ChannelId);
                return;
            }

            if (!route.BackendBinding.Matches(
                    frame.BackendKind,
                    frame.NodeId,
                    frame.MasterConnectionId,
                    frame.DirectConnectionId))
            {
                throw new InvalidOperationException("Backend route close used a Backend session that does not match the route binding.");
            }

            if (route.State != PersistentBackendRouteState.Open)
            {
                throw new InvalidOperationException("Persistent Backend route is not open.");
            }

            if (!m_PersistentBackendRouteRegistry.Close(route.RouteToken, close.Reason))
            {
                throw new InvalidOperationException("Persistent Backend route could not be closed.");
            }

            await WriteBackendRouteCloseNotifyAsync(
                route.Owner,
                new GatewayBackendRouteClose(route.RouteToken, close.Reason),
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
                "Gateway rejected Backend route close frame. BackendKind={BackendKind}, NodeId={NodeId}.",
                frame.BackendKind,
                frame.NodeId);
        }
    }

    private async ValueTask OnBackendRouteSessionClosedAsync(
        BackendRouteSessionClosed frame,
        CancellationToken cancellationToken)
    {
        var routes = m_PersistentBackendRouteRegistry.RemoveBackendBindingRoutes(
            frame.Binding,
            frame.Reason);
        foreach (var route in routes)
        {
            try
            {
                await WriteBackendRouteCloseNotifyAsync(
                    route.Owner,
                    new GatewayBackendRouteClose(route.RouteToken, frame.Reason),
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
                    "Gateway could not notify client that a Backend route session closed. BackendKind={BackendKind}, NodeId={NodeId}.",
                    frame.Binding.BackendKind,
                    frame.Binding.NodeId);
            }
        }
    }

    private void RequirePrincipalExchangeCreationAllowed(
        string? principalSubjectId,
        int limit,
        ConcurrentDictionary<string, FixedWindowRateCounter> counters,
        string directionName)
    {
        if (limit <= 0 ||
            string.IsNullOrWhiteSpace(principalSubjectId))
        {
            return;
        }

        var normalizedPrincipalSubjectId = principalSubjectId.Trim();
        var counter = counters.GetOrAdd(
            normalizedPrincipalSubjectId,
            static _ => new FixedWindowRateCounter());
        counter.IncrementOrThrow(
            limit,
            m_BackendRouteOptions.ExchangeRateLimitWindowMilliseconds,
            DateTimeOffset.UtcNow,
            $"Gateway persistent Backend route principal {directionName} exchange creation rate limit was exceeded.");
    }

    private void RequirePacketManifestMatch(
        PersistentBackendRoute<Client> route,
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        ReadOnlySpan<byte> payload)
    {
        var result = backendPacketManifests.ValidatePacket(
            route.BackendKind,
            route.ManifestId,
            route.ManifestHash,
            direction,
            packetKind,
            packetId,
            routedVersion,
            payload);
        if (result.Success)
        {
            return;
        }

        var counterKey = $"{direction}:{result.Failure}";
        m_PacketManifestRejectCounters.AddOrUpdate(
            counterKey,
            static _ => 1,
            static (_, current) => current + 1);
        throw new InvalidOperationException(
            $"Backend route packet failed manifest validation. Direction={direction}, Failure={result.Failure}, BackendKind={route.BackendKind}, ManifestId={route.ManifestId.Value}, PacketKind={packetKind}, PacketId={packetId}, Version={routedVersion}, PayloadLength={payload.Length}.");
    }

    private async Task EchoPacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        if (packet.Header.Kind != PacketKind.Request)
        {
            return;
        }

        var payloadString = Encoding.UTF8.GetString(packet.Payload.Span);
        Console.WriteLine(payloadString);

        using var response = PacketFrame.Create(
            PacketKind.Response,
            packet.Header.PacketId,
            packet.Header.Version,
            Encoding.UTF8.GetBytes($"Response: {payloadString}"));
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendRouteResponseAsync(
        Client client,
        ushort version,
        GatewayBackendRouteResponse routeResponse,
        CancellationToken cancellationToken)
    {
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE,
            version,
            routeResponse,
            GatewayBackendRouteResponse.Codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendRouteOpenResponseAsync(
        Client client,
        ushort version,
        GatewayBackendRouteOpenResponse routeResponse,
        CancellationToken cancellationToken)
    {
        if (!TryGetBackendRouteOpenResponseCodec(version, out var codec))
        {
            logger.LogWarning(
                "Gateway skipped Backend route open response for unsupported protocol version {ProtocolVersion}.",
                version);
            return;
        }

        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            version,
            routeResponse,
            codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendRouteCloseResponseAsync(
        Client client,
        ushort version,
        GatewayBackendRouteClose routeClose,
        CancellationToken cancellationToken)
    {
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_CLOSE,
            version,
            routeClose,
            GatewayBackendRouteClose.Codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendServerListResponseAsync(
        Client client,
        ushort version,
        GatewayBackendServerListResponse serverListResponse,
        CancellationToken cancellationToken)
    {
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_SERVER_LIST,
            version,
            serverListResponse,
            GatewayBackendServerListResponse.Codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBackendRouteCloseNotifyAsync(
        Client client,
        GatewayBackendRouteClose routeClose,
        CancellationToken cancellationToken)
    {
        using var clientFrame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE_CLOSE,
            GatewayBackendRouteClose.ProtocolVersion,
            routeClose,
            GatewayBackendRouteClose.Codec);
        await client.WriteAsync(clientFrame, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteClientAuthenticateResponseAsync(
        Client client,
        ushort version,
        GatewayClientAuthenticateResponse authenticationResponse,
        CancellationToken cancellationToken)
    {
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_CLIENT_AUTHENTICATE,
            version,
            authenticationResponse,
            GatewayClientAuthenticateResponse.Codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteClientAuthenticationOptionsResponseAsync(
        Client client,
        ushort version,
        GatewayClientAuthenticationOptionsResponse authenticationOptionsResponse,
        CancellationToken cancellationToken)
    {
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_CLIENT_AUTHENTICATION_OPTIONS,
            version,
            authenticationOptionsResponse,
            GatewayClientAuthenticationOptionsResponse.Codec);
        await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        return
        [
            .. certificateProvider.GetStatusItems(),
            .. backendPacketManifests.GetStatusItems(),
            .. GetPacketManifestRejectStatusItems(),
            .. m_PersistentBackendRouteRegistry.GetStatusItems()
        ];
    }

    private ServiceAdminStatusItem[] GetPacketManifestRejectStatusItems()
    {
        return
        [
            .. m_PacketManifestRejectCounters
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new ServiceAdminStatusItem(
                    "Backend packet manifest rejects",
                    pair.Key,
                    pair.Value.ToString()))
        ];
    }

    private void RemoveBackendRoutes(Client client)
    {
        var routes = m_PersistentBackendRouteRegistry.RemoveOwnerRoutesAndReturn(client, "client disconnected");
        m_ServerListClientCounters.TryRemove(client, out _);
        if (routes.Length == 0)
        {
            return;
        }

        _ = NotifyBackendRoutesClosedAsync(routes, "client disconnected", CancellationToken.None);
    }

    private async Task NotifyBackendRoutesClosedAsync(
        PersistentBackendRoute<Client>[] routes,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var route in routes)
        {
            await NotifyBackendRouteClosedAsync(route, reason, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyBackendRouteClosedAsync(
        PersistentBackendRoute<Client> route,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            using var closeFrame = PacketCodec.Encode(
                PacketKind.Notify,
                Pid.GATE_BACKEND_CHANNEL_CLOSE,
                GatewayBackendChannelClose.ProtocolVersion,
                new GatewayBackendChannelClose(route.ChannelId, reason),
                GatewayBackendChannelClose.Codec);
            await backendRouteManager.RelayFrameAsync(
                route.BackendBinding,
                closeFrame,
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
                "Gateway could not notify Backend that a persistent route closed. BackendKind={BackendKind}, NodeId={NodeId}, ChannelId={ChannelId}.",
                route.BackendKind,
                route.BackendBinding.NodeId,
                route.ChannelId);
        }
    }

    private async Task NotifyBackendRouteOpenedAsync(
        PersistentBackendRoute<Client> route,
        CancellationToken cancellationToken)
    {
        using var openFrame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_OPEN,
            GatewayBackendChannelOpen.ProtocolVersion,
            new GatewayBackendChannelOpen(route.ChannelId, route.PrincipalSubjectId),
            GatewayBackendChannelOpen.Codec);
        await backendRouteManager.RelayFrameAsync(
            route.BackendBinding,
            openFrame,
            cancellationToken).ConfigureAwait(false);
    }

    private static Guid GetResponseRouteId(Guid routeId)
    {
        return routeId == Guid.Empty ? Guid.NewGuid() : routeId;
    }

    private static bool IsBackendRoutePacket(PacketFrame packet)
    {
        return packet.Header.PacketId is
            Pid.GATE_BACKEND_ROUTE or
            Pid.GATE_BACKEND_ROUTE_OPEN or
            Pid.GATE_BACKEND_SERVER_LIST or
            Pid.GATE_BACKEND_ROUTE_DATA or
            Pid.GATE_BACKEND_ROUTE_CLOSE;
    }

    private void RequireAuthenticationMethodAllowed(
        GatewayClientAuthenticationContext authenticationContext,
        string backendKind,
        GatewayBackendServerHandle? serverHandle)
    {
        var principal = authenticationContext.Principal
            ?? throw new InvalidOperationException("Authenticated Gateway client context is missing a principal.");
        var methods = backendRouteManager.GetAuthenticationMethods(backendKind, serverHandle);
        if (methods.Length == 0)
        {
            throw new UnauthorizedAccessException("No Gateway authentication methods are available for the selected Backend server.");
        }

        foreach (var method in methods)
        {
            if (method.Kind == principal.AuthenticationMethodKind &&
                string.Equals(method.MethodId, principal.AuthenticationMethodId, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new UnauthorizedAccessException("Gateway client authentication method is not allowed for the selected Backend server.");
    }

    private static (Guid RouteId, string BackendKind) GetBackendRouteResponseIdentity(PacketFrame packet)
    {
        try
        {
            if (packet.Header.Version == GatewayBackendRouteEnvelope.ProtocolVersion)
            {
                var envelope = PacketCodec.Decode(packet, GatewayBackendRouteEnvelope.Codec);
                return (GetResponseRouteId(envelope.RouteId), GetResponseBackendKind(envelope.BackendKind));
            }
        }
        catch
        {
        }

        return (Guid.NewGuid(), "unknown");
    }

    private static string GetBackendRouteOpenResponseBackendKind(PacketFrame packet)
    {
        try
        {
            if (TryGetBackendRouteOpenRequestCodec(packet.Header.Version, out var codec))
            {
                var request = PacketCodec.Decode(packet, codec);
                return GetResponseBackendKind(request.BackendKind);
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static GatewayBackendRouteOpenRequest DecodeBackendRouteOpenRequest(PacketFrame packet)
    {
        if (!TryGetBackendRouteOpenRequestCodec(packet.Header.Version, out var codec))
        {
            throw new InvalidOperationException($"Unsupported Backend route open protocol version {packet.Header.Version}.");
        }

        return PacketCodec.Decode(packet, codec);
    }

    private static bool TryGetBackendRouteOpenRequestCodec(
        ushort version,
        out IPacketCodec<GatewayBackendRouteOpenRequest> codec)
    {
        if (version == GatewayBackendRouteOpenRequest.LegacyProtocolVersion)
        {
            codec = GatewayBackendRouteOpenRequest.LegacyCodec;
            return true;
        }

        if (version == GatewayBackendRouteOpenRequest.ProtocolVersion)
        {
            codec = GatewayBackendRouteOpenRequest.Codec;
            return true;
        }

        codec = GatewayBackendRouteOpenRequest.Codec;
        return false;
    }

    private static bool TryGetBackendRouteOpenResponseCodec(
        ushort version,
        out IPacketCodec<GatewayBackendRouteOpenResponse> codec)
    {
        if (version == GatewayBackendRouteOpenResponse.LegacyProtocolVersion)
        {
            codec = GatewayBackendRouteOpenResponse.LegacyCodec;
            return true;
        }

        if (version == GatewayBackendRouteOpenResponse.ProtocolVersion)
        {
            codec = GatewayBackendRouteOpenResponse.Codec;
            return true;
        }

        codec = GatewayBackendRouteOpenResponse.Codec;
        return false;
    }

    private static string GetBackendServerListResponseBackendKind(PacketFrame packet)
    {
        try
        {
            if (packet.Header.Version == GatewayBackendServerListRequest.ProtocolVersion)
            {
                var request = PacketCodec.Decode(packet, GatewayBackendServerListRequest.Codec);
                return GetResponseBackendKind(request.BackendKind);
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private void RequireServerListAllowed(
        Client client,
        string? principalSubjectId)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var limit = m_BackendRouteOptions.MaxServerListRequestsPerClientPerWindow;
        if (limit > 0)
        {
            var counter = m_ServerListClientCounters.GetOrAdd(
                client,
                static _ => new FixedWindowRateCounter());
            counter.IncrementOrThrow(
                limit,
                GetServerListRateLimitWindowMilliseconds(),
                DateTimeOffset.UtcNow,
                "Gateway Backend server list rate limit was exceeded.");
        }

        var normalizedPrincipalSubjectId = string.IsNullOrWhiteSpace(principalSubjectId)
            ? null
            : principalSubjectId.Trim();
        var principalLimit = m_BackendRouteOptions.MaxServerListRequestsPerPrincipalPerWindow;
        if (normalizedPrincipalSubjectId == null ||
            principalLimit <= 0)
        {
            return;
        }

        var principalCounter = m_ServerListPrincipalCounters.GetOrAdd(
            normalizedPrincipalSubjectId,
            static _ => new FixedWindowRateCounter());
        principalCounter.IncrementOrThrow(
            principalLimit,
            GetServerListRateLimitWindowMilliseconds(),
            DateTimeOffset.UtcNow,
            "Gateway Backend server list principal rate limit was exceeded.");
    }

    private int GetServerListEntryLimit(GatewayBackendServerListRequest request)
    {
        var configuredLimit = m_BackendRouteOptions.MaxServerListEntries <= 0
            ? GatewayBackendServerListRequest.MaxRequestedEntries
            : Math.Min(
                m_BackendRouteOptions.MaxServerListEntries,
                GatewayBackendServerListRequest.MaxRequestedEntries);
        return request.MaximumEntries <= 0
            ? configuredLimit
            : Math.Min(request.MaximumEntries, configuredLimit);
    }

    private int GetServerListRateLimitWindowMilliseconds()
    {
        return Math.Max(1, m_BackendRouteOptions.ServerListRateLimitWindowMilliseconds);
    }

    private static string GetResponseBackendKind(string backendKind)
    {
        return string.IsNullOrWhiteSpace(backendKind) ? "unknown" : backendKind.Trim();
    }

    private static string GetAuthenticationErrorMessage(string errorMessage)
    {
        return string.IsNullOrWhiteSpace(errorMessage)
            ? "Gateway client authentication was rejected."
            : errorMessage;
    }

    private async Task DisposeClientsAsync()
    {
        Client[] clients;
        lock (m_Clients)
        {
            clients = [.. m_Clients];
        }

        foreach (var client in clients)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Gateway client disposal completed with an error during shutdown.");
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

    private static async ValueTask DisposeHandshakeStreamsAsync(
        NetworkStream networkStream,
        Stream? authenticatedStream)
    {
        if (authenticatedStream != null &&
            !ReferenceEquals(authenticatedStream, networkStream))
        {
            await authenticatedStream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        await networkStream.DisposeAsync().ConfigureAwait(false);
    }
}
