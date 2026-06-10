using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugHandshakeHello
{
    public RemoteDebugHandshakeHello(
        string clientId,
        string displayName,
        string clientVersion,
        string unityVersion,
        RemoteDebugCapabilities capabilities,
        ushort protocolVersion)
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
        ProtocolVersion = protocolVersion;
    }

    public string ClientId { get; }

    public string DisplayName { get; }

    public string ClientVersion { get; }

    public string UnityVersion { get; }

    public RemoteDebugCapabilities Capabilities { get; }

    public ushort ProtocolVersion { get; }

    public static IPacketCodec<RemoteDebugHandshakeHello> Codec { get; } = new RemoteDebugHandshakeHelloCodec();

    private sealed class RemoteDebugHandshakeHelloCodec : IPacketCodec<RemoteDebugHandshakeHello>
    {
        public int GetPayloadSize(RemoteDebugHandshakeHello value)
        {
            return sizeof(ushort) +
                   PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.ClientVersion) +
                   PacketWriter.GetStringSize(value.UnityVersion) +
                   sizeof(uint);
        }

        public void Encode(RemoteDebugHandshakeHello value, ref PacketWriter writer)
        {
            writer.WriteUInt16(value.ProtocolVersion);
            writer.WriteString(value.ClientId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.ClientVersion);
            writer.WriteString(value.UnityVersion);
            writer.WriteUInt32((uint)value.Capabilities);
        }

        public RemoteDebugHandshakeHello Decode(ref PacketReader reader)
        {
            ushort protocolVersion = reader.ReadUInt16();
            string clientId = reader.ReadString();
            string displayName = reader.ReadString();
            string clientVersion = reader.ReadString();
            string unityVersion = reader.ReadString();
            var capabilities = (RemoteDebugCapabilities)reader.ReadUInt32();

            return new RemoteDebugHandshakeHello(
                clientId,
                displayName,
                clientVersion,
                unityVersion,
                capabilities,
                protocolVersion);
        }
    }
}
