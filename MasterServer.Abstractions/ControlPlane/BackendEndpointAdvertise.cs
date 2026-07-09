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
            BackendServerDescriptor.DefaultOpen.DescriptorJson,
            Array.Empty<GatewayAuthenticationMethodDefinition>())
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
        : this(
            backendKind,
            gatewayEndpoint,
            manifestId,
            manifestHash,
            state,
            descriptorVersion,
            descriptorJson,
            Array.Empty<GatewayAuthenticationMethodDefinition>())
    {
    }

    public BackendEndpointAdvertise(
        string backendKind,
        MasterSocketEndpoint gatewayEndpoint,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        BackendNodeState state,
        string descriptorVersion,
        string descriptorJson,
        GatewayAuthenticationMethodDefinition[] authenticationMethods)
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
        AuthenticationMethods = authenticationMethods ?? throw new ArgumentNullException(nameof(authenticationMethods));
    }

    public string BackendKind { get; }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public BackendPacketManifestId ManifestId { get; }

    public BackendPacketManifestHash ManifestHash { get; }

    public BackendServerDescriptor Descriptor { get; }

    public GatewayAuthenticationMethodDefinition[] AuthenticationMethods { get; }

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
                   PacketWriter.GetStringSize(value.Descriptor.DescriptorJson) +
                   GetAuthenticationMethodsSize(value.AuthenticationMethods);
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
            WriteAuthenticationMethods(value.AuthenticationMethods, ref writer);
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
                reader.ReadString(),
                ReadAuthenticationMethods(ref reader));
        }

        private static int GetAuthenticationMethodsSize(GatewayAuthenticationMethodDefinition[] value)
        {
            int size = sizeof(int);
            foreach (var method in value)
            {
                size += GatewayAuthenticationMethodDefinition.Codec.GetPayloadSize(method);
            }

            return size;
        }

        private static void WriteAuthenticationMethods(
            GatewayAuthenticationMethodDefinition[] value,
            ref PacketWriter writer)
        {
            writer.WriteInt32(value.Length);
            foreach (var method in value)
            {
                GatewayAuthenticationMethodDefinition.Codec.Encode(method, ref writer);
            }
        }

        private static GatewayAuthenticationMethodDefinition[] ReadAuthenticationMethods(ref PacketReader reader)
        {
            const int MAX_METHOD_COUNT = 32;
            var methodCount = reader.ReadInt32();
            if (methodCount < 0 || methodCount > MAX_METHOD_COUNT)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway authentication method count.");
            }

            var methods = new GatewayAuthenticationMethodDefinition[methodCount];
            for (int i = 0; i < methods.Length; i++)
            {
                methods[i] = GatewayAuthenticationMethodDefinition.Codec.Decode(ref reader);
            }

            return methods;
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
