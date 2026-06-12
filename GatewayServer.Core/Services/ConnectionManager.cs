using System.Diagnostics;
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

internal class ConnectionManager(
    IOptions<ConnectionManagerOptions> options,
    IOptions<BackendRouteOptions> backendRouteOptions,
    IBackendRouteManager backendRouteManager,
    ILogger<ConnectionManager> logger,
    IHostEnvironment env) : IHostedService, IConnectionManager
{
    private readonly BackendRouteOptions m_BackendRouteOptions = backendRouteOptions.Value;
    private readonly CancellationTokenSource m_GracefulCancellation = new();

    private Socket? m_Socket;
    private Task? m_AcceptTask;
    private X509Certificate2? m_Cert;
    private readonly HashSet<Client> m_Clients = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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
        await m_GracefulCancellation.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();

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
            EnsureBackendKindAllowed(backendKind);

            using var routedFrame = envelope.CreateRoutedFrame();
            await backendRouteManager.RelayFrameAsync(
                backendKind,
                routedFrame,
                cancellationToken).ConfigureAwait(false);

            if (packet.Header.Kind == PacketKind.Request)
            {
                await WriteBackendRouteResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteResponse.Accepted(backendKind),
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
                await WriteBackendRouteResponseAsync(
                    client,
                    packet.Header.Version,
                    GatewayBackendRouteResponse.Rejected(backendKind, "Backend route was rejected."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
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
}
