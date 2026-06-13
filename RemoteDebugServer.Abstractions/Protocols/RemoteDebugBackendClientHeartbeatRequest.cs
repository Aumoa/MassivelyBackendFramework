using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientHeartbeatRequest
{
    public RemoteDebugBackendClientHeartbeatRequest(string clientId, string sessionId)
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
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public static IPacketCodec<RemoteDebugBackendClientHeartbeatRequest> Codec { get; } = new RemoteDebugBackendClientHeartbeatRequestCodec();

    private sealed class RemoteDebugBackendClientHeartbeatRequestCodec : IPacketCodec<RemoteDebugBackendClientHeartbeatRequest>
    {
        public int GetPayloadSize(RemoteDebugBackendClientHeartbeatRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.SessionId);
        }

        public void Encode(RemoteDebugBackendClientHeartbeatRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.SessionId);
        }

        public RemoteDebugBackendClientHeartbeatRequest Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientHeartbeatRequest(
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
