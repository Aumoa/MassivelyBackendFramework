using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendEndpointAdvertise
{
    public BackendEndpointAdvertise(
        string backendKind,
        MasterSocketEndpoint gatewayEndpoint,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash)
        : this(
            backendKind,
            gatewayEndpoint,
            manifestId,
            manifestHash,
            BackendServerDescriptor.DefaultOpen.State,
            BackendServerDescriptor.DefaultOpen.DescriptorVersion,
            BackendServerDescriptor.DefaultOpen.DescriptorJson)
    {
    }

    public BackendEndpointAdvertise(
        string backendKind,
        MasterSocketEndpoint gatewayEndpoint,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        BackendNodeState state,
        string descriptorVersion,
        string descriptorJson)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        BackendKind = backendKind;
        GatewayEndpoint = gatewayEndpoint ?? throw new ArgumentNullException(nameof(gatewayEndpoint));
        ManifestId = manifestId;
        ManifestHash = manifestHash;
        Descriptor = BackendServerDescriptor.Create(state, descriptorVersion, descriptorJson);
    }

    public string BackendKind { get; }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public BackendPacketManifestId ManifestId { get; }

    public BackendPacketManifestHash ManifestHash { get; }

    public BackendServerDescriptor Descriptor { get; }

    public static IPacketCodec<BackendEndpointAdvertise> Codec { get; } = new BackendEndpointAdvertiseCodec();

    private sealed class BackendEndpointAdvertiseCodec : IPacketCodec<BackendEndpointAdvertise>
    {
        public int GetPayloadSize(BackendEndpointAdvertise value)
        {
            return PacketWriter.GetStringSize(value.BackendKind) +
                   GetEndpointSize(value.GatewayEndpoint) +
                   PacketWriter.GetStringSize(value.ManifestId.Value) +
                   PacketWriter.GetStringSize(value.ManifestHash.Value) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.Descriptor.DescriptorVersion) +
                   PacketWriter.GetStringSize(value.Descriptor.DescriptorJson);
        }

        public void Encode(BackendEndpointAdvertise value, ref PacketWriter writer)
        {
            writer.WriteString(value.BackendKind);
            WriteEndpoint(value.GatewayEndpoint, ref writer);
            writer.WriteString(value.ManifestId.Value);
            writer.WriteString(value.ManifestHash.Value);
            writer.WriteByte((byte)value.Descriptor.State);
            writer.WriteString(value.Descriptor.DescriptorVersion);
            writer.WriteString(value.Descriptor.DescriptorJson);
        }

        public BackendEndpointAdvertise Decode(ref PacketReader reader)
        {
            string backendKind = reader.ReadString();
            var endpoint = ReadEndpoint(ref reader);
            return new BackendEndpointAdvertise(
                backendKind,
                endpoint,
                new BackendPacketManifestId(reader.ReadString()),
                new BackendPacketManifestHash(reader.ReadString()),
                (BackendNodeState)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString());
        }

        private static int GetEndpointSize(MasterSocketEndpoint value)
        {
            return PacketWriter.GetStringSize(value.IPAddress) + sizeof(int) + sizeof(byte);
        }

        private static void WriteEndpoint(MasterSocketEndpoint value, ref PacketWriter writer)
        {
            writer.WriteString(value.IPAddress);
            writer.WriteInt32(value.Port);
            writer.WriteByte(value.UseTls ? (byte)1 : (byte)0);
        }

        private static MasterSocketEndpoint ReadEndpoint(ref PacketReader reader)
        {
            string ipAddress = reader.ReadString();
            int port = reader.ReadInt32();
            bool useTls = reader.ReadByte() != 0;
            return new MasterSocketEndpoint(ipAddress, port, useTls);
        }
    }
}
