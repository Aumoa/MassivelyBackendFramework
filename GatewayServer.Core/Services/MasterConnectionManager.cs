using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using GatewayServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace GatewayServer.Services;

internal sealed class MasterConnectionManager(
    IOptions<MasterConnectionOptions> options,
    ILogger<MasterConnectionManager> logger) : IHostedService
{
    private readonly MasterConnectionOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private Task? m_RunTask;
    private int m_Trusted;

    public bool IsTrusted => Volatile.Read(ref m_Trusted) == 1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            logger.LogInformation("Gateway Master control-plane connection is disabled.");
            return Task.CompletedTask;
        }

        EnsureConfigured();
        m_RunTask = RunAsync(m_Shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Gateway Master control-plane session ended before trust was maintained.");
            }
            finally
            {
                MarkTrusted(false);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Max(1, m_Options.ReconnectDelayMilliseconds)),
                    cancellationToken).ConfigureAwait(false);
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

        await socket.ConnectAsync(new IPEndPoint(address, m_Options.Port), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Gateway connected to Master socket at {Address}:{Port}.", m_Options.IPAddress, m_Options.Port);

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
            MarkTrusted(true);
            logger.LogInformation(
                "Gateway Master control-plane session trusted. NodeId={NodeId}, MasterConnectionId={ConnectionId}.",
                accepted.NodeId,
                accepted.ConnectionId);

            await DrainTrustedFramesAsync(activeStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
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

        using var challengeFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.NodeAuthChallenge,
            handshakeTimeout.Token).ConfigureAwait(false);
        var challenge = PacketCodec.Decode(challengeFrame, NodeAuthChallenge.Codec);

        var hello = new NodeHello(
            MasterNodeKind.Gateway,
            m_Options.NodeId,
            m_Options.DisplayName,
            MasterControlProtocol.SchemaVersion);

        using (var helloFrame = PacketCodec.Encode(
                   PacketKind.Control,
                   MasterControlPacketIds.NodeHello,
                   MasterControlProtocol.SchemaVersion,
                   hello,
                   NodeHello.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, helloFrame, handshakeTimeout.Token).ConfigureAwait(false);
        }

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

        using var acceptedFrame = await ReadRequiredHandshakeFrameAsync(
            stream,
            MasterControlPacketIds.NodeAccepted,
            handshakeTimeout.Token).ConfigureAwait(false);
        var accepted = PacketCodec.Decode(acceptedFrame, NodeAccepted.Codec);

        if (!string.Equals(accepted.NodeId, hello.NodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Master accepted a different node id than the Gateway requested.");
        }

        return accepted;
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
            throw new EndOfStreamException("Master connection closed before Gateway handshake completed.");
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

    private static async Task DrainTrustedFramesAsync(Stream stream, CancellationToken cancellationToken)
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
            }
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

    private void MarkTrusted(bool trusted)
    {
        Volatile.Write(ref m_Trusted, trusted ? 1 : 0);
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
