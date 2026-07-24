using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendNodeSnapshot
{
    private const int MaxBackendNodeCount = 8192;

    public BackendNodeSnapshot(BackendNodeEndpoint[] nodes, DateTimeOffset observedAt)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        ObservedAt = observedAt;
    }

    public BackendNodeEndpoint[] Nodes { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<BackendNodeSnapshot> Codec { get; } = new BackendNodeSnapshotCodec();

    private sealed class BackendNodeSnapshotCodec : IPacketCodec<BackendNodeSnapshot>
    {
        public int GetPayloadSize(BackendNodeSnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = sizeof(int) + sizeof(long);
            foreach (var node in value.Nodes)
            {
                size += GetNodeSize(node);
            }

            return size;
        }

        public void Encode(BackendNodeSnapshot value, ref PacketWriter writer)
        {
            writer.WriteInt32(value.Nodes.Length);
            foreach (var node in value.Nodes)
            {
                WriteNode(node, ref writer);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public BackendNodeSnapshot Decode(ref PacketReader reader)
        {
            int nodeCount = reader.ReadInt32();
            if (nodeCount < 0 || nodeCount > MaxBackendNodeCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid backend node count.");
            }

            var nodes = new BackendNodeEndpoint[nodeCount];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = ReadNode(ref reader);
            }

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new BackendNodeSnapshot(nodes, observedAt);
        }

        private static int GetNodeSize(BackendNodeEndpoint value)
        {
            return PacketWriter.GetStringSize(value.BackendKind) +
                   PacketWriter.GetStringSize(value.NodeId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.MasterConnectionId) +
                   GetEndpointSize(value.GatewayEndpoint) +
                   PacketWriter.GetStringSize(value.ManifestId.Value) +
                   PacketWriter.GetStringSize(value.ManifestHash.Value) +
                   GetDescriptorSize(value.Descriptor) +
                   GetAuthenticationMethodsSize(value.AuthenticationMethods) +
                   sizeof(long);
        }

        private static void WriteNode(BackendNodeEndpoint value, ref PacketWriter writer)
        {
            writer.WriteString(value.BackendKind);
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.MasterConnectionId);
            WriteEndpoint(value.GatewayEndpoint, ref writer);
            writer.WriteString(value.ManifestId.Value);
            writer.WriteString(value.ManifestHash.Value);
            WriteDescriptor(value.Descriptor, ref writer);
            WriteAuthenticationMethods(value.AuthenticationMethods, ref writer);
            writer.WriteInt64(value.AdvertisedAt.ToUnixTimeMilliseconds());
        }

        private static BackendNodeEndpoint ReadNode(ref PacketReader reader)
        {
            string backendKind = reader.ReadString();
            string nodeId = reader.ReadString();
            string displayName = reader.ReadString();
            string masterConnectionId = reader.ReadString();
            var gatewayEndpoint = ReadEndpoint(ref reader);
            var manifestId = new BackendPacketManifestId(reader.ReadString());
            var manifestHash = new BackendPacketManifestHash(reader.ReadString());
            var descriptor = ReadDescriptor(ref reader);
            var authenticationMethods = ReadAuthenticationMethods(ref reader);
            var advertisedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new BackendNodeEndpoint(
                backendKind,
                nodeId,
                displayName,
                masterConnectionId,
                gatewayEndpoint,
                manifestId,
                manifestHash,
                descriptor,
                authenticationMethods,
                advertisedAt);
        }

        private static int GetDescriptorSize(BackendServerDescriptor value)
        {
            return sizeof(byte) +
                   PacketWriter.GetStringSize(value.DescriptorVersion) +
                   PacketWriter.GetStringSize(value.DescriptorHash) +
                   PacketWriter.GetStringSize(value.DescriptorJson);
        }

        private static void WriteDescriptor(BackendServerDescriptor value, ref PacketWriter writer)
        {
            writer.WriteByte((byte)value.State);
            writer.WriteString(value.DescriptorVersion);
            writer.WriteString(value.DescriptorHash);
            writer.WriteString(value.DescriptorJson);
        }

        private static BackendServerDescriptor ReadDescriptor(ref PacketReader reader)
        {
            return new BackendServerDescriptor(
                (BackendNodeState)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
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
