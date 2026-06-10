using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugHandshakeAccepted
{
    public RemoteDebugHandshakeAccepted(
        string clientId,
        string sessionId,
        RemoteDebugCapabilities enabledCapabilities,
        int heartbeatIntervalMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (heartbeatIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalMilliseconds));
        }

        ClientId = clientId;
        SessionId = sessionId;
        EnabledCapabilities = enabledCapabilities;
        HeartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public RemoteDebugCapabilities EnabledCapabilities { get; }

    public int HeartbeatIntervalMilliseconds { get; }

    public static IPacketCodec<RemoteDebugHandshakeAccepted> Codec { get; } = new RemoteDebugHandshakeAcceptedCodec();

    private sealed class RemoteDebugHandshakeAcceptedCodec : IPacketCodec<RemoteDebugHandshakeAccepted>
    {
        public int GetPayloadSize(RemoteDebugHandshakeAccepted value)
        {
            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.SessionId) +
                   sizeof(uint) +
                   sizeof(int);
        }

        public void Encode(RemoteDebugHandshakeAccepted value, ref PacketWriter writer)
        {
            writer.WriteString(value.ClientId);
            writer.WriteString(value.SessionId);
            writer.WriteUInt32((uint)value.EnabledCapabilities);
            writer.WriteInt32(value.HeartbeatIntervalMilliseconds);
        }

        public RemoteDebugHandshakeAccepted Decode(ref PacketReader reader)
        {
            string clientId = reader.ReadString();
            string sessionId = reader.ReadString();
            var enabledCapabilities = (RemoteDebugCapabilities)reader.ReadUInt32();
            int heartbeatIntervalMilliseconds = reader.ReadInt32();

            return new RemoteDebugHandshakeAccepted(
                clientId,
                sessionId,
                enabledCapabilities,
                heartbeatIntervalMilliseconds);
        }
    }
}
