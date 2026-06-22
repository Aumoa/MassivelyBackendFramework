using System.Collections.Concurrent;
using System.Net.Sockets;
using BackendServer.Runtime;
using GatewayServer.Protocols;
using Microsoft.Extensions.Logging;
using PacketCore;

namespace BackendServer.Services;

internal sealed class GatewayChannelSender(ILogger<GatewayChannelSender> logger) : IBackendGatewayChannelSender
{
    private readonly ConcurrentDictionary<Guid, GatewayConnectionSession> m_Sessions = [];

    public ValueTask SendNotifyAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        return SendChannelDataAsync(
            channel,
            PacketKind.Notify,
            packetId,
            version,
            exchangeId: null,
            payload,
            cancellationToken);
    }

    public ValueTask SendRequestAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        Guid exchangeId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        return SendChannelDataAsync(
            channel,
            PacketKind.Request,
            packetId,
            version,
            exchangeId,
            payload,
            cancellationToken);
    }

    public ValueTask SendResponseAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        Guid exchangeId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        return SendChannelDataAsync(
            channel,
            PacketKind.Response,
            packetId,
            version,
            exchangeId,
            payload,
            cancellationToken);
    }

    public async ValueTask CloseAsync(
        BackendGatewayChannel channel,
        string reason,
        CancellationToken cancellationToken)
    {
        var session = RequireSession(channel);
        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_CLOSE,
            GatewayBackendChannelClose.ProtocolVersion,
            new GatewayBackendChannelClose(channel.ChannelId, reason),
            GatewayBackendChannelClose.Codec);
        await session.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    internal void AttachSession(
        Guid connectionId,
        string gatewayNodeId,
        Stream stream)
    {
        var session = new GatewayConnectionSession(connectionId, gatewayNodeId, stream);
        if (!m_Sessions.TryAdd(connectionId, session))
        {
            session.Dispose();
            throw new InvalidOperationException("Gateway connection is already attached for Backend channel sends.");
        }
    }

    internal void DetachSession(Guid connectionId)
    {
        if (m_Sessions.TryRemove(connectionId, out var session))
        {
            session.Dispose();
        }
    }

    private async ValueTask SendChannelDataAsync(
        BackendGatewayChannel channel,
        PacketKind kind,
        ushort packetId,
        ushort version,
        Guid? exchangeId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var session = RequireSession(channel);
        GatewayBackendExchangeId? protocolExchangeId = exchangeId.HasValue
            ? new GatewayBackendExchangeId(exchangeId.Value)
            : null;
        var envelope = new GatewayBackendChannelDataEnvelope(
            channel.ChannelId,
            kind,
            packetId,
            version,
            protocolExchangeId,
            payload.ToArray());
        using var frame = PacketCodec.Encode(
            kind,
            Pid.GATE_BACKEND_CHANNEL_DATA,
            GatewayBackendChannelDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendChannelDataEnvelope.Codec);
        await session.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private GatewayConnectionSession RequireSession(BackendGatewayChannel channel)
    {
        if (m_Sessions.TryGetValue(channel.GatewayConnectionId, out var session))
        {
            return session;
        }

        logger.LogWarning(
            "Backend attempted to send to a Gateway connection that is not active. GatewayConnectionId={GatewayConnectionId}, ChannelId={ChannelId}.",
            channel.GatewayConnectionId,
            channel.ChannelId);
        throw new InvalidOperationException("Gateway connection is not active.");
    }

    private sealed class GatewayConnectionSession(
        Guid connectionId,
        string gatewayNodeId,
        Stream stream) : IDisposable
    {
        private readonly SemaphoreSlim m_WriteLock = new(1, 1);

        public async ValueTask WriteAsync(PacketFrame frame, CancellationToken cancellationToken)
        {
            await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRemoteDisconnect(exception))
            {
                throw new IOException(
                    $"Gateway connection '{gatewayNodeId}' ({connectionId:N}) closed while writing Backend channel data.",
                    exception);
            }
            finally
            {
                m_WriteLock.Release();
            }
        }

        public void Dispose()
        {
            m_WriteLock.Dispose();
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
    }
}
