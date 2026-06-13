using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientSnapshot
{
    public RemoteDebugBackendClientSnapshot(
        string clientId,
        string displayName,
        string clientVersion,
        string unityVersion,
        RemoteDebugCapabilities capabilities,
        long connectedAtUnixTimeMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ClientId = clientId;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ClientVersion = clientVersion ?? throw new ArgumentNullException(nameof(clientVersion));
        UnityVersion = unityVersion ?? throw new ArgumentNullException(nameof(unityVersion));
        Capabilities = capabilities;
        ConnectedAtUnixTimeMilliseconds = connectedAtUnixTimeMilliseconds;
    }

    public string ClientId { get; }

    public string DisplayName { get; }

    public string ClientVersion { get; }

    public string UnityVersion { get; }

    public RemoteDebugCapabilities Capabilities { get; }

    public long ConnectedAtUnixTimeMilliseconds { get; }

    public static IPacketCodec<RemoteDebugBackendClientSnapshot> Codec { get; } = new RemoteDebugBackendClientSnapshotCodec();

    private sealed class RemoteDebugBackendClientSnapshotCodec : IPacketCodec<RemoteDebugBackendClientSnapshot>
    {
        public int GetPayloadSize(RemoteDebugBackendClientSnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.ClientVersion) +
                   PacketWriter.GetStringSize(value.UnityVersion) +
                   sizeof(uint) +
                   sizeof(long);
        }

        public void Encode(RemoteDebugBackendClientSnapshot value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ClientId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.ClientVersion);
            writer.WriteString(value.UnityVersion);
            writer.WriteUInt32((uint)value.Capabilities);
            writer.WriteInt64(value.ConnectedAtUnixTimeMilliseconds);
        }

        public RemoteDebugBackendClientSnapshot Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendClientSnapshot(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                (RemoteDebugCapabilities)reader.ReadUInt32(),
                reader.ReadInt64());
        }
    }
}
