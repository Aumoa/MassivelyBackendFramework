using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientDisconnectNotify
{
    public RemoteDebugBackendClientDisconnectNotify(
        string clientId,
        string sessionId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        ClientId = clientId;
        SessionId = sessionId;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public string Reason { get; }

    public static IPacketCodec<RemoteDebugBackendClientDisconnectNotify> Codec { get; } = new RemoteDebugBackendClientDisconnectNotifyCodec();

    private sealed class RemoteDebugBackendClientDisconnectNotifyCodec : IPacketCodec<RemoteDebugBackendClientDisconnectNotify>
    {
        public int GetPayloadSize(RemoteDebugBackendClientDisconnectNotify value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.SessionId) +
                   PacketWriter.GetStringSize(value.Reason);
        }

        public void Encode(RemoteDebugBackendClientDisconnectNotify value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.SessionId);
            writer.WriteString(value.Reason);
        }

        public RemoteDebugBackendClientDisconnectNotify Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientDisconnectNotify(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
