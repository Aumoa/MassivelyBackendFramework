using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
    IGatewayClientCertificateLoader certificateLoader,
    IGatewayClientStreamAuthenticator streamAuthenticator,
    IGatewayClientAuthenticationContextFactory authenticationContextFactory,
    IGatewayBackendRouteTokenGenerator routeTokenGenerator,
    ILogger<ConnectionManager> logger,
    IHostEnvironment env) : IHostedService, IConnectionManager, IBackendRouteStatusProvider
{
    private readonly CancellationTokenSource m_GracefulCancellation = new();
    private readonly BackendRouteOptions m_BackendRouteOptions = backendRouteOptions.Value;
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_ClientOriginPrincipalExchangeCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_BackendOriginPrincipalExchangeCounters = new(StringComparer.Ordinal);
    private readonly BackendRouteRegistry<Client> m_BackendRouteRegistry = new(backendRouteOptions.Value, logger);
    private readonly PersistentBackendRouteRegistry<Client> m_PersistentBackendRouteRegistry = new(
        backendRouteOptions.Value,
        routeTokenGenerator,
        logger);

    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private X509Certificate2? m_Cert;
    private readonly HashSet<Client> m_Clients = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionOptions = options.Value;
        if (!connectionOptions.UseTls)
        {
            throw new InvalidOperationException("Gateway client listener requires TLS. Development may use a private or self-signed certificate, but plaintext TCP is not supported.");
        }

        m_Cert = await certificateLoader
            .LoadAsync(connectionOptions, env.IsDevelopment(), cancellationToken)
            .ConfigureAwait(false);

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
            m_Cert?.Dispose();
            m_Cert = null;
            throw;
        }

        backendRouteManager.RouteFrameReceived += OnBackendRouteFrameReceivedAsync;
        backendRouteManager.RouteDataFrameReceived += OnBackendRouteDataFrameReceivedAsync;
        backendRouteManager.RouteCloseFrameReceived += OnBackendRouteCloseFrameReceivedAsync;
        backendRouteManager.RouteSessionClosed += OnBackendRouteSessionClosedAsync;
        logger.LogInformation("Gateway client listener is running on {Address}:{Port} with TLS.", connectionOptions.IPAddress, connectionOptions.Port);

        m_AcceptTask = StartAcceptAsync(m_GracefulCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        backendRouteManager.RouteFrameReceived -= OnBackendRouteFrameReceivedAsync;
        backendRouteManager.RouteDataFrameReceived -= OnBackendRouteDataFrameReceivedAsync;
        backendRouteManager.RouteCloseFrameReceived -= OnBackendRouteCloseFrameReceivedAsync;
        backendRouteManager.RouteSessionClosed -= OnBackendRouteSessionClosedAsync;
        await m_GracefulCancellation.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();
        m_BackendRouteRegistry.CancelAll();
        m_PersistentBackendRouteRegistry.CancelAll();

        if (m_AcceptTask != null)
        {
            await WaitForShutdownAsync(m_AcceptTask, cancellationToken).ConfigureAwait(false);
        }

        await DisposeClientsAsync().ConfigureAwait(false);
        m_Cert?.Dispose();
        m_Cert = null;
    }

    private async Task StartAcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? clientSocket = null;

            try
            {
                clientSocket = await m_Socket!.AcceptAsync(cancellationToken).ConfigureAwait(false);
                StartHandshakeAsync(clientSocket, m_Cert!, cancellationToken);
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

    private async void StartHandshakeAsync(Socket socket, X509Certificate2 serverCert, CancellationToken cancellationToken)
    {
        socket.NoDelay = true;

        var networkStream = new NetworkStream(socket, ownsSocket: true);
        Stream? s = null;

        try
        {
            s = await streamAuthenticator
                .AuthenticateAsync(networkStream, serverCert, cancellationToken)
                .ConfigureAwait(false);

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
            await DisposeHandshakeStreamsAsync(networkStream, s).ConfigureAwait(false);
            socket.Dispose();

            return;
        }
        catch (Exception e)
        {
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
            m_BackendRouteRegistry.RecordLegacyRoutePacket(packet.Header.Kind);
            if (!m_BackendRouteOptions.EnableLegacyOneShotRoutes)
            {
                await RejectDisabledLegacyBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
                return;
            }

            await HandleBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE_OPEN)
        {
            await HandleBackendRouteOpenPacketAsync(client, authenticationContext, packet, cancellationToken).ConfigureAwait(false);
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

    private async Task HandleBackendRoutePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        var backendKind = string.Empty;
        var routeId = Guid.Empty;
        var routeRegistered = false;

        try
        {
            if (packet.Header.Kind is not (PacketKind.Request or PacketKind.Notify))
            {
                throw new InvalidOperationException("Backend route packets must be Request or Notify packets.");
            }

            if (packet.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Backend route protocol version {packet.Header.Version}.");
            }

            var envelope = PacketCodec.Decode(packet, GatewayBackendRouteEnvelope.Codec);
            backendKind = envelope.BackendKind;
            routeId = envelope.RouteId;
            if (envelope.RoutedKind is not (PacketKind.Request or PacketKind.Notify))
            {
                throw new InvalidOperationException("Client Backend route envelopes must contain Request or Notify packets.");
            }

            if (packet.Header.Kind != envelope.RoutedKind)
            {
                throw new InvalidOperationException("Backend route packet kind must match the routed packet kind.");
            }

            backendKind = m_BackendRouteRegistry.RequireAllowedBackendKind(backendKind);

            if (envelope.RoutedKind == PacketKind.Request)
            {
                m_BackendRouteRegistry.Register(routeId, backendKind, client, m_GracefulCancellation.Token);
                routeRegistered = true;
            }

            using var routedFrame = PacketCodec.Encode(
                envelope.RoutedKind,
                Pid.GATE_BACKEND_ROUTE,
                GatewayBackendRouteEnvelope.ProtocolVersion,
                envelope,
                GatewayBackendRouteEnvelope.Codec);
            await backendRouteManager.RelayFrameAsync(
                backendKind,
                routedFrame,
                cancellationToken).ConfigureAwait(false);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteBackendRouteResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteResponse.Accepted(routeId, backendKind),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Gateway rejected Backend route packet. BackendKind={BackendKind}, PacketKind={PacketKind}, PacketId={PacketId}.",
                backendKind,
                packet.Header.Kind,
                packet.Header.PacketId);

            if (packet.Header.Kind == PacketKind.Request)
            {
                if (routeRegistered)
                {
                    m_BackendRouteRegistry.Remove(routeId);
                }

                await WriteBackendRouteResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteResponse.Rejected(GetResponseRouteId(routeId), backendKind, "Backend route was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleBackendRouteOpenPacketAsync(
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
                throw new InvalidOperationException("Backend route open packets must be Request packets.");
            }

            if (packet.Header.Version != GatewayBackendRouteOpenRequest.ProtocolVersion)
            {
                throw new InvalidOperationException($"Unsupported Backend route open protocol version {packet.Header.Version}.");
            }

            var request = PacketCodec.Decode(packet, GatewayBackendRouteOpenRequest.Codec);
            backendKind = request.BackendKind;
            var principalSubjectId = authenticationContext.Principal?.SubjectId;
            m_PersistentBackendRouteRegistry.RequireOpenAttemptAllowed(client, principalSubjectId);
            var normalizedBackendKind = m_PersistentBackendRouteRegistry.RequireAllowedBackendKind(request.BackendKind);
            var backendSession = await backendRouteManager
                .ConnectAsync(normalizedBackendKind, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(backendSession.BackendKind, normalizedBackendKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Selected Backend session kind does not match the requested Backend kind.");
            }

            var route = m_PersistentBackendRouteRegistry.Open(
                backendSession.Binding,
                client,
                principalSubjectId,
                m_GracefulCancellation.Token);

            await WriteBackendRouteOpenResponseAsync(
                client,
                packet.Header.Version,
                GatewayBackendRouteOpenResponse.Accepted(route.RouteToken, route.BackendKind),
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

            using var routedFrame = PacketCodec.Encode(
                envelope.RoutedKind,
                Pid.GATE_BACKEND_ROUTE_DATA,
                GatewayBackendRouteDataEnvelope.ProtocolVersion,
                envelope,
                GatewayBackendRouteDataEnvelope.Codec);
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
        }
    }

    private async Task RejectDisabledLegacyBackendRoutePacketAsync(
        Client client,
        PacketFrame packet,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Gateway rejected disabled legacy Backend route packet. PacketKind={PacketKind}, PacketId={PacketId}.",
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
            GatewayBackendRouteResponse.Rejected(routeId, backendKind, "Legacy Backend route flow is disabled."),
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

    private async ValueTask OnBackendRouteFrameReceivedAsync(
        BackendRouteFrameReceived frame,
        CancellationToken cancellationToken)
    {
        if (!m_BackendRouteRegistry.TryGet(frame.Envelope.RouteId, out var pendingRoute))
        {
            logger.LogWarning(
                "Gateway received Backend route frame for an unknown client route. BackendKind={BackendKind}, RouteId={RouteId}.",
                frame.BackendKind,
                frame.Envelope.RouteId);
            return;
        }

        if (frame.Envelope.RoutedKind == PacketKind.Response)
        {
            m_BackendRouteRegistry.Remove(frame.Envelope.RouteId);
        }

        using var clientFrame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            frame.Envelope,
            GatewayBackendRouteEnvelope.Codec);
        await pendingRoute.Owner.WriteAsync(clientFrame, cancellationToken).ConfigureAwait(false);
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
            if (envelope.Direction != GatewayBackendRouteDirection.BackendToClient)
            {
                throw new InvalidOperationException("Backend route data frames from Backend must use the BackendToClient direction.");
            }

            if (!m_PersistentBackendRouteRegistry.TryGet(envelope.RouteToken, out route))
            {
                logger.LogWarning(
                    "Gateway received Backend route data for an unknown route token. BackendKind={BackendKind}.",
                    frame.BackendKind);
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

            using var clientFrame = PacketCodec.Encode(
                envelope.RoutedKind,
                Pid.GATE_BACKEND_ROUTE_DATA,
                GatewayBackendRouteDataEnvelope.ProtocolVersion,
                envelope,
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
            if (!m_PersistentBackendRouteRegistry.TryGet(close.RouteToken, out route))
            {
                logger.LogWarning(
                    "Gateway received Backend route close for an unknown route token. BackendKind={BackendKind}, NodeId={NodeId}.",
                    frame.BackendKind,
                    frame.NodeId);
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

            if (!m_PersistentBackendRouteRegistry.Close(close.RouteToken, close.Reason))
            {
                throw new InvalidOperationException("Persistent Backend route could not be closed.");
            }

            await WriteBackendRouteCloseNotifyAsync(route.Owner, close, cancellationToken).ConfigureAwait(false);
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
        using var response = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            version,
            routeResponse,
            GatewayBackendRouteOpenResponse.Codec);
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

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        return
        [
            .. m_BackendRouteRegistry.GetStatusItems(),
            .. m_PersistentBackendRouteRegistry.GetStatusItems()
        ];
    }

    private void RemoveBackendRoutes(Client client)
    {
        m_BackendRouteRegistry.RemoveOwnerRoutes(client);
        m_PersistentBackendRouteRegistry.RemoveOwnerRoutes(client, "client disconnected");
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
            Pid.GATE_BACKEND_ROUTE_DATA or
            Pid.GATE_BACKEND_ROUTE_CLOSE;
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
            if (packet.Header.Version == GatewayBackendRouteOpenRequest.ProtocolVersion)
            {
                var request = PacketCodec.Decode(packet, GatewayBackendRouteOpenRequest.Codec);
                return GetResponseBackendKind(request.BackendKind);
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static string GetResponseBackendKind(string backendKind)
    {
        return string.IsNullOrWhiteSpace(backendKind) ? "unknown" : backendKind.Trim();
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
