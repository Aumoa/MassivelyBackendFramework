using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PacketCore;
using RemoteDebugServer.Protocols;

namespace RemoteDebug;

public sealed class RemoteDebugClient : IDisposable, IAsyncDisposable
{
    private readonly TcpClient m_TcpClient;
    private readonly Stream m_Stream;
    private int m_Disposed;

    private RemoteDebugClient(
        TcpClient tcpClient,
        Stream stream,
        RemoteDebugSessionInfo session)
    {
        m_TcpClient = tcpClient;
        m_Stream = stream;
        Session = session;
    }

    public RemoteDebugSessionInfo Session { get; }

    public static async Task<RemoteDebugClient> ConnectAsync(
        RemoteDebugClientOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();

        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromMilliseconds(options.HandshakeTimeoutMilliseconds));

        TcpClient? tcpClient = null;
        Stream? activeStream = null;

        try
        {
            tcpClient = new TcpClient
            {
                NoDelay = true
            };

            await WaitWithCancellationAsync(
                tcpClient.ConnectAsync(options.Host, options.Port),
                handshakeTimeout.Token).ConfigureAwait(false);

            var networkStream = tcpClient.GetStream();
            activeStream = networkStream;

            if (options.UseTls)
            {
                var sslStream = CreateSslStream(networkStream, options);
                activeStream = sslStream;
                await WaitWithCancellationAsync(
                    sslStream.AuthenticateAsClientAsync(
                        string.IsNullOrWhiteSpace(options.ServerName) ? options.Host : options.ServerName,
                        clientCertificates: null,
                        enabledSslProtocols: options.EnabledSslProtocols,
                        checkCertificateRevocation: options.CheckCertificateRevocation),
                    handshakeTimeout.Token).ConfigureAwait(false);
            }

            var session = await CompleteHandshakeAsync(
                activeStream,
                options,
                handshakeTimeout.Token).ConfigureAwait(false);
            var client = new RemoteDebugClient(tcpClient, activeStream, session);
            tcpClient = null;
            activeStream = null;
            return client;
        }
        finally
        {
            activeStream?.Dispose();
            tcpClient?.Dispose();
        }
    }

    public ValueTask WriteFrameAsync(PacketFrame frame, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return PacketFrameWriter.WriteAsync(m_Stream, frame, cancellationToken);
    }

    public ValueTask<PacketFrame?> ReadFrameAsync(
        PacketReadPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return PacketFrameReader.ReadAsync(m_Stream, policy, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_Stream.Dispose();
        m_TcpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        await m_Stream.DisposeAsync().ConfigureAwait(false);
        m_TcpClient.Dispose();
    }

    private static SslStream CreateSslStream(NetworkStream networkStream, RemoteDebugClientOptions options)
    {
        return options.ServerCertificateValidationCallback == null
            ? new SslStream(networkStream, leaveInnerStreamOpen: false)
            : new SslStream(networkStream, leaveInnerStreamOpen: false, options.ServerCertificateValidationCallback);
    }

    private static async Task<RemoteDebugSessionInfo> CompleteHandshakeAsync(
        Stream stream,
        RemoteDebugClientOptions options,
        CancellationToken cancellationToken)
    {
        using var challengeFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            RemoteDebugPacketIds.HandshakeChallenge,
            cancellationToken).ConfigureAwait(false);
        var challenge = PacketCodec.Decode(challengeFrame, RemoteDebugAuthChallenge.Codec);

        var hello = new RemoteDebugHandshakeHello(
            options.ClientId,
            options.DisplayName,
            options.ClientVersion,
            options.UnityVersion,
            options.Capabilities,
            RemoteDebugProtocol.SchemaVersion);

        using (var helloFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   RemoteDebugPacketIds.HandshakeHello,
                   RemoteDebugProtocol.SchemaVersion,
                   hello,
                   RemoteDebugHandshakeHello.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, helloFrame, cancellationToken).ConfigureAwait(false);
        }

        var proof = new RemoteDebugAuthProof(
            options.ClientId,
            RemoteDebugClientAuthenticator.ComputeProof(challenge, hello, options.SharedSecret));
        using (var proofFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   RemoteDebugPacketIds.HandshakeProof,
                   RemoteDebugProtocol.SchemaVersion,
                   proof,
                   RemoteDebugAuthProof.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, proofFrame, cancellationToken).ConfigureAwait(false);
        }

        using var acceptedFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            RemoteDebugPacketIds.HandshakeAccepted,
            cancellationToken).ConfigureAwait(false);
        var accepted = PacketCodec.Decode(acceptedFrame, RemoteDebugHandshakeAccepted.Codec);
        ValidateAccepted(options, accepted);

        return new RemoteDebugSessionInfo(
            accepted.ClientId,
            accepted.SessionId,
            accepted.EnabledCapabilities,
            accepted.HeartbeatIntervalMilliseconds);
    }

    private static async ValueTask<PacketFrame> ReadRequiredHandshakeFrameAsync(
        Stream stream,
        ushort expectedPacketId,
        CancellationToken cancellationToken)
    {
        var frame = await PacketFrameReader.ReadAsync(
            stream,
            RemoteDebugProtocol.UntrustedHandshakePolicy,
            cancellationToken).ConfigureAwait(false);

        if (frame == null)
        {
            throw new EndOfStreamException("RemoteDebug server closed the connection before the handshake completed.");
        }

        try
        {
            RemoteDebugProtocol.ValidateHandshakeFrame(frame, expectedPacketId);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private static void ValidateAccepted(
        RemoteDebugClientOptions options,
        RemoteDebugHandshakeAccepted accepted)
    {
        if (!string.Equals(accepted.ClientId, options.ClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RemoteDebug server accepted a different client id.");
        }

        if ((accepted.EnabledCapabilities & ~options.Capabilities) != 0)
        {
            throw new InvalidOperationException("RemoteDebug server enabled capabilities that the client did not request.");
        }
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion))
        {
            if (task != await Task.WhenAny(task, completion.Task).ConfigureAwait(false))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        await task.ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref m_Disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RemoteDebugClient));
        }
    }
}
