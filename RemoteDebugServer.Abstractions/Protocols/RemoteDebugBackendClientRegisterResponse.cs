using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientRegisterResponse
{
    public RemoteDebugBackendClientRegisterResponse(
        string clientId,
        string sessionId,
        RemoteDebugCapabilities enabledCapabilities,
        int heartbeatIntervalMilliseconds,
        long registeredAtUnixTimeMilliseconds)
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
        RegisteredAtUnixTimeMilliseconds = registeredAtUnixTimeMilliseconds;
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public RemoteDebugCapabilities EnabledCapabilities { get; }

    public int HeartbeatIntervalMilliseconds { get; }

    public long RegisteredAtUnixTimeMilliseconds { get; }

    public static IPacketCodec<RemoteDebugBackendClientRegisterResponse> Codec { get; } = new RemoteDebugBackendClientRegisterResponseCodec();

    private sealed class RemoteDebugBackendClientRegisterResponseCodec : IPacketCodec<RemoteDebugBackendClientRegisterResponse>
    {
        public int GetPayloadSize(RemoteDebugBackendClientRegisterResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.SessionId) +
                   sizeof(uint) +
                   sizeof(int) +
                   sizeof(long);
        }

        public void Encode(RemoteDebugBackendClientRegisterResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.SessionId);
            writer.WriteUInt32((uint)value.EnabledCapabilities);
            writer.WriteInt32(value.HeartbeatIntervalMilliseconds);
            writer.WriteInt64(value.RegisteredAtUnixTimeMilliseconds);
        }

        public RemoteDebugBackendClientRegisterResponse Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientRegisterResponse(
                reader.ReadString(),
                reader.ReadString(),
                (RemoteDebugCapabilities)reader.ReadUInt32(),
                reader.ReadInt32(),
                reader.ReadInt64());
        }
    }
}
