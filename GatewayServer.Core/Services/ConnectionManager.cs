using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
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
    ILogger<ConnectionManager> logger,
    IHostEnvironment env) : IHostedService, IConnectionManager, IBackendRouteStatusProvider
{
    private readonly BackendRouteOptions m_BackendRouteOptions = backendRouteOptions.Value;
    private readonly CancellationTokenSource m_GracefulCancellation = new();
    private readonly ConcurrentDictionary<Guid, PendingBackendRoute> m_PendingBackendRoutes = [];

    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private X509Certificate2? m_Cert;
    private readonly HashSet<Client> m_Clients = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        backendRouteManager.RouteFrameReceived += OnBackendRouteFrameReceivedAsync;

        if (options.Value.UseTls)
        {
            if (env.IsDevelopment())
            {
                m_Cert = await LoadDevelopmentCertAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var listenAddress = await MasterEndpointResolver.ResolveBindAddressAsync(
            options.Value.IPAddress,
            cancellationToken).ConfigureAwait(false);
        m_Socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (listenAddress.Equals(IPAddress.IPv6Any))
        {
            m_Socket.DualMode = true;
        }

        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        m_Socket.Bind(new IPEndPoint(listenAddress, options.Value.Port));
        m_Socket.Listen();
        logger.LogInformation("Gateway client listener is running on {Address}:{Port}.", options.Value.IPAddress, options.Value.Port);

        m_AcceptTask = StartAcceptAsync(m_GracefulCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        backendRouteManager.RouteFrameReceived -= OnBackendRouteFrameReceivedAsync;
        await m_GracefulCancellation.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();
        CancelPendingBackendRoutes();

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
                StartHandshakeAsync(clientSocket, m_Cert, cancellationToken);
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

    private async void StartHandshakeAsync(Socket socket, X509Certificate2? serverCert, CancellationToken cancellationToken)
    {
        socket.NoDelay = true;

        var networkStream = new NetworkStream(socket, ownsSocket: true);
        SslStream sslStream = null!;
        if (m_Cert != null)
        {
            sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        }

        Stream s;

        try
        {
            if (sslStream != null && serverCert != null)
            {
                await sslStream.AuthenticateAsServerAsync(serverCert, clientCertificateRequired: false, enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13, checkCertificateRevocation: true);
                s = sslStream;
            }
            else
            {
                s = networkStream;
            }

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
            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }

            await networkStream.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();

            return;
        }
        catch (Exception e)
        {
            logger.LogError("Error during handshake: {Message}", e.Message);

            if (sslStream != null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }

            await networkStream.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();

            return;
        }

        var client = new Client(networkStream, s, logger);

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

                client.Start();
                HandleClientPacketsAsync(client, cancellationToken);
            }
        }

        if (!addedToClients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await networkStream.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();
        }
    }

    private async void HandleClientPacketsAsync(Client client, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in client.ReadPacketsAsync(cancellationToken))
            {
                if (packet.Header.PacketId == Pid.GATE_BACKEND_ROUTE)
                {
                    await HandleBackendRoutePacketAsync(client, packet, cancellationToken).ConfigureAwait(false);
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

            EnsureBackendKindAllowed(backendKind);

            if (envelope.RoutedKind == PacketKind.Request)
            {
                RegisterBackendRoute(routeId, backendKind, client);
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
                    RemoveBackendRoute(routeId);
                }

                await WriteBackendRouteResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteResponse.Rejected(GetResponseRouteId(routeId), backendKind, "Backend route was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask OnBackendRouteFrameReceivedAsync(
        BackendRouteFrameReceived frame,
        CancellationToken cancellationToken)
    {
        if (!m_PendingBackendRoutes.TryGetValue(frame.Envelope.RouteId, out var pendingRoute))
        {
            logger.LogWarning(
                "Gateway received Backend route frame for an unknown client route. BackendKind={BackendKind}, RouteId={RouteId}.",
                frame.BackendKind,
                frame.Envelope.RouteId);
            return;
        }

        if (frame.Envelope.RoutedKind == PacketKind.Response)
        {
            RemoveBackendRoute(frame.Envelope.RouteId);
        }

        using var clientFrame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            frame.Envelope,
            GatewayBackendRouteEnvelope.Codec);
        await pendingRoute.Client.WriteAsync(clientFrame, cancellationToken).ConfigureAwait(false);
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

    private void EnsureBackendKindAllowed(string backendKind)
    {
        var allowedBackendKinds = m_BackendRouteOptions.AllowedBackendKinds ?? [];
        if (allowedBackendKinds.Any(candidate => string.Equals(
                candidate?.Trim(),
                backendKind,
                StringComparison.Ordinal)))
        {
            return;
        }

        throw new UnauthorizedAccessException($"Backend kind '{backendKind}' is not enabled for client routing.");
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        var pendingRoutes = m_PendingBackendRoutes.Values.ToArray();
        var allowedBackendKinds = m_BackendRouteOptions.AllowedBackendKinds ?? [];
        var items = new List<ServiceAdminStatusItem>
        {
            new("Backend routes", "Allowed kinds", allowedBackendKinds.Length == 0
                ? "None"
                : string.Join(", ", allowedBackendKinds.OrderBy(static item => item, StringComparer.Ordinal))),
            new("Backend routes", "Pending routes", pendingRoutes.Length.ToString()),
            new("Backend routes", "Request timeout", $"{GetBackendRouteTimeoutMilliseconds()} ms")
        };

        foreach (var group in pendingRoutes
                     .GroupBy(static route => route.BackendKind, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new ServiceAdminStatusItem($"Backend route {group.Key}", "Pending routes", group.Count().ToString()));
        }

        return [.. items];
    }

    private void RegisterBackendRoute(
        Guid routeId,
        string backendKind,
        Client client)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingRoute = new PendingBackendRoute(
            routeId,
            backendKind,
            client,
            now,
            now.AddMilliseconds(GetBackendRouteTimeoutMilliseconds()),
            CancellationTokenSource.CreateLinkedTokenSource(m_GracefulCancellation.Token));

        if (!m_PendingBackendRoutes.TryAdd(routeId, pendingRoute))
        {
            pendingRoute.TimeoutCancellation.Cancel();
            pendingRoute.TimeoutCancellation.Dispose();
            throw new InvalidOperationException("Backend route id is already active.");
        }

        pendingRoute.TimeoutTask = ExpireBackendRouteAsync(pendingRoute);
    }

    private async Task ExpireBackendRouteAsync(PendingBackendRoute pendingRoute)
    {
        try
        {
            var delay = pendingRoute.ExpiresAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, pendingRoute.TimeoutCancellation.Token).ConfigureAwait(false);
            }

            if (m_PendingBackendRoutes.TryRemove(pendingRoute.RouteId, out _))
            {
                logger.LogWarning(
                    "Gateway Backend route expired. BackendKind={BackendKind}, RouteId={RouteId}, CreatedAt={CreatedAt:O}, ExpiresAt={ExpiresAt:O}.",
                    pendingRoute.BackendKind,
                    pendingRoute.RouteId,
                    pendingRoute.CreatedAt,
                    pendingRoute.ExpiresAt);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Gateway Backend route expiration task failed. BackendKind={BackendKind}, RouteId={RouteId}.",
                pendingRoute.BackendKind,
                pendingRoute.RouteId);
        }
        finally
        {
            pendingRoute.TimeoutCancellation.Dispose();
        }
    }

    private bool RemoveBackendRoute(Guid routeId)
    {
        if (!m_PendingBackendRoutes.TryRemove(routeId, out var pendingRoute))
        {
            return false;
        }

        pendingRoute.TimeoutCancellation.Cancel();
        return true;
    }

    private void RemoveBackendRoutes(Client client)
    {
        foreach (var pair in m_PendingBackendRoutes.ToArray())
        {
            if (ReferenceEquals(pair.Value.Client, client))
            {
                RemoveBackendRoute(pair.Key);
            }
        }
    }

    private void CancelPendingBackendRoutes()
    {
        foreach (var routeId in m_PendingBackendRoutes.Keys)
        {
            RemoveBackendRoute(routeId);
        }
    }

    private static Guid GetResponseRouteId(Guid routeId)
    {
        return routeId == Guid.Empty ? Guid.NewGuid() : routeId;
    }

    private int GetBackendRouteTimeoutMilliseconds()
    {
        return Math.Max(1000, m_BackendRouteOptions.RequestTimeoutMilliseconds);
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

    private static async Task<X509Certificate2> LoadDevelopmentCertAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certs = store.Certificates.Find(X509FindType.FindBySubjectName, "localhost", false);

            if (certs.Count == 0)
            {
                throw new InvalidOperationException("No development certificate found. Please create a self-signed certificate with the subject name 'localhost' and install it in the LocalMachine/My store.");
            }

            return certs[0];
        }, cancellationToken);
    }

    private sealed class PendingBackendRoute(
        Guid routeId,
        string backendKind,
        Client client,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationTokenSource timeoutCancellation)
    {
        public Guid RouteId { get; } = routeId;

        public string BackendKind { get; } = backendKind;

        public Client Client { get; } = client;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public CancellationTokenSource TimeoutCancellation { get; } = timeoutCancellation;

        public Task? TimeoutTask { get; set; }
    }
}
