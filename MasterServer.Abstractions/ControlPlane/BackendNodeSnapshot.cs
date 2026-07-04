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
            var advertisedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new BackendNodeEndpoint(
                backendKind,
                nodeId,
                displayName,
                masterConnectionId,
                gatewayEndpoint,
                manifestId,
                manifestHash,
                advertisedAt);
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
