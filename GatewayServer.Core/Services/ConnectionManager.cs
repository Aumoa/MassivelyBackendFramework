using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using GatewayServer.Behaviors;
using GatewayServer.Options;
using GatewayServer.Protocols;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal class ConnectionManager(IOptions<ConnectionManagerOptions> options, ILogger<ConnectionManager> logger, IHostEnvironment env) : IHostedService, IConnectionManager
{
    private readonly Socket m_Socket = new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
    private readonly CancellationTokenSource m_GracefulCancellation = new();

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

        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var ep = new IPEndPoint(
            IPAddress.Parse(options.Value.IPAddress),
            options.Value.Port
            );
        m_Socket.Bind(ep);
        m_Socket.Listen();
        m_AcceptTask = StartAcceptAsync(m_GracefulCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        m_GracefulCancellation.Cancel();
        if (m_AcceptTask != null)
        {
            await m_AcceptTask.ConfigureAwait(false);
        }
    }

    private async Task StartAcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var clientSocket = await m_Socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            StartHandshakeAsync(clientSocket, m_Cert, cancellationToken);
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
                SimpleEcho(client, cancellationToken);
            }
        }

        if (!addedToClients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await networkStream.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();
        }
    }

    private async void SimpleEcho(Client client, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in client.ReadPacketsAsync(cancellationToken))
            {
                var payloadString = Encoding.UTF8.GetString(packet.Payload.Span);
                Console.WriteLine(payloadString);

                var responseMessage = $"Response: {payloadString}";
                using var response = PacketFrame.Create(
                    PacketKind.Response,
                    packet.Header.PacketId,
                    packet.Header.Version,
                    Encoding.UTF8.GetBytes(responseMessage));
                await client.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error occurred while echoing packets.");
        }
        finally
        {
            await client.DisposeAsync();
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
