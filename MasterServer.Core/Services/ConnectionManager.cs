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
    ILogger<ConnectionManager> logger) : IHostedService, IConnectionManager
{
    private readonly MasterSocketOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly ConcurrentDictionary<Guid, MasterConnection> m_Connections = [];
    private readonly ConcurrentDictionary<Guid, Task> m_ConnectionTasks = [];

    private Socket? m_Socket;
    private Task? m_AcceptTask;
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
        if (string.IsNullOrWhiteSpace(m_Options.NodeAuthSecret))
        {
            throw new InvalidOperationException("MasterSocket:NodeAuthSecret must be configured before accepting node connections.");
        }

        if (m_Options.UseTls)
        {
            m_Cert = await LoadCertificateAsync(m_Options, cancellationToken).ConfigureAwait(false);
        }

        var listenAddress = IPAddress.Parse(m_Options.IPAddress);
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

            await AuthenticateNodeAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
            await DrainUntilClosedAsync(connection, activeStream, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Master node connection ended with an error.");
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
                connection.MarkSeen();
                NotifyConnectionsChanged();
            }
        }
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

        if (!MasterNodeAuthenticator.VerifyProof(challenge, hello, proof, m_Options.NodeAuthSecret))
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

        connection.MarkAccepted(hello.NodeKind, hello.NodeId);
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

        public Guid ConnectionId { get; } = Guid.NewGuid();

        public Socket Socket { get; } = socket;

        public MasterNodeKind NodeKind { get; private set; } = MasterNodeKind.Unknown;

        public string NodeId { get; private set; } = string.Empty;

        public string RemoteEndPoint { get; } = socket.RemoteEndPoint?.ToString() ?? "unknown";

        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

        public bool IsTrusted => Volatile.Read(ref m_Trusted) == 1;

        public void MarkAccepted(MasterNodeKind nodeKind, string nodeId)
        {
            NodeKind = nodeKind;
            NodeId = nodeId;
            MarkSeen();
            Volatile.Write(ref m_Trusted, 1);
        }

        public void MarkSeen()
        {
            Interlocked.Exchange(ref m_LastSeenAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public MasterConnectionSnapshot ToSnapshot()
        {
            return new MasterConnectionSnapshot(
                ConnectionId,
                RemoteEndPoint,
                IsTrusted ? NodeKind : MasterNodeKind.Unknown,
                ConnectedAt,
                DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref m_LastSeenAt)));
        }

        public void Dispose()
        {
            Socket.Dispose();
        }
    }
}
