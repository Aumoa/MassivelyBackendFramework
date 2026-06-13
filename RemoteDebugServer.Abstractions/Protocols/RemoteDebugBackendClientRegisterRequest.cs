using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientRegisterRequest
{
    public RemoteDebugBackendClientRegisterRequest(
        string clientId,
        string displayName,
        string clientVersion,
        string unityVersion,
        RemoteDebugCapabilities requestedCapabilities)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ClientId = clientId;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ClientVersion = clientVersion ?? throw new ArgumentNullException(nameof(clientVersion));
        UnityVersion = unityVersion ?? throw new ArgumentNullException(nameof(unityVersion));
        RequestedCapabilities = requestedCapabilities;
    }

    public string ClientId { get; }

    public string DisplayName { get; }

    public string ClientVersion { get; }

    public string UnityVersion { get; }

    public RemoteDebugCapabilities RequestedCapabilities { get; }

    public static IPacketCodec<RemoteDebugBackendClientRegisterRequest> Codec { get; } = new RemoteDebugBackendClientRegisterRequestCodec();

    private sealed class RemoteDebugBackendClientRegisterRequestCodec : IPacketCodec<RemoteDebugBackendClientRegisterRequest>
    {
        public int GetPayloadSize(RemoteDebugBackendClientRegisterRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.ClientVersion) +
                   PacketWriter.GetStringSize(value.UnityVersion) +
                   sizeof(uint);
        }

        public void Encode(RemoteDebugBackendClientRegisterRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.ClientVersion);
            writer.WriteString(value.UnityVersion);
            writer.WriteUInt32((uint)value.RequestedCapabilities);
        }

        public RemoteDebugBackendClientRegisterRequest Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientRegisterRequest(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                (RemoteDebugCapabilities)reader.ReadUInt32());
        }
    }
}
