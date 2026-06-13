using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientHeartbeatResponse
{
    public RemoteDebugBackendClientHeartbeatResponse(
        string clientId,
        string sessionId,
        long observedAtUnixTimeMilliseconds)
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
        ObservedAtUnixTimeMilliseconds = observedAtUnixTimeMilliseconds;
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public long ObservedAtUnixTimeMilliseconds { get; }

    public static IPacketCodec<RemoteDebugBackendClientHeartbeatResponse> Codec { get; } = new RemoteDebugBackendClientHeartbeatResponseCodec();

    private sealed class RemoteDebugBackendClientHeartbeatResponseCodec : IPacketCodec<RemoteDebugBackendClientHeartbeatResponse>
    {
        public int GetPayloadSize(RemoteDebugBackendClientHeartbeatResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.SessionId) +
                   sizeof(long);
        }

        public void Encode(RemoteDebugBackendClientHeartbeatResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.SessionId);
            writer.WriteInt64(value.ObservedAtUnixTimeMilliseconds);
        }

        public RemoteDebugBackendClientHeartbeatResponse Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientHeartbeatResponse(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt64());
        }
    }
}
