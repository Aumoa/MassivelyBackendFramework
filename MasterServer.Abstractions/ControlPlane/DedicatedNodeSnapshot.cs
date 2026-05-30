using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DedicatedNodeSnapshot
{
    private const int MaxDedicatedNodeCount = 8192;

    public DedicatedNodeSnapshot(DedicatedNodeEndpoint[] nodes, DateTimeOffset observedAt)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        ObservedAt = observedAt;
    }

    public DedicatedNodeEndpoint[] Nodes { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<DedicatedNodeSnapshot> Codec { get; } = new DedicatedNodeSnapshotCodec();

    private sealed class DedicatedNodeSnapshotCodec : IPacketCodec<DedicatedNodeSnapshot>
    {
        public int GetPayloadSize(DedicatedNodeSnapshot value)
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

        public void Encode(DedicatedNodeSnapshot value, ref PacketWriter writer)
        {
            writer.WriteInt32(value.Nodes.Length);
            foreach (var node in value.Nodes)
            {
                WriteNode(node, ref writer);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public DedicatedNodeSnapshot Decode(ref PacketReader reader)
        {
            int nodeCount = reader.ReadInt32();
            if (nodeCount < 0 || nodeCount > MaxDedicatedNodeCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Dedicated node count.");
            }

            var nodes = new DedicatedNodeEndpoint[nodeCount];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = ReadNode(ref reader);
            }

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new DedicatedNodeSnapshot(nodes, observedAt);
        }

        private static int GetNodeSize(DedicatedNodeEndpoint value)
        {
            return PacketWriter.GetStringSize(value.NodeId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.MasterConnectionId) +
                   GetEndpointSize(value.GatewayEndpoint) +
                   sizeof(long);
        }

        private static void WriteNode(DedicatedNodeEndpoint value, ref PacketWriter writer)
        {
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.MasterConnectionId);
            WriteEndpoint(value.GatewayEndpoint, ref writer);
            writer.WriteInt64(value.AdvertisedAt.ToUnixTimeMilliseconds());
        }

        private static DedicatedNodeEndpoint ReadNode(ref PacketReader reader)
        {
            string nodeId = reader.ReadString();
            string displayName = reader.ReadString();
            string masterConnectionId = reader.ReadString();
            var gatewayEndpoint = ReadEndpoint(ref reader);
            var advertisedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new DedicatedNodeEndpoint(nodeId, displayName, masterConnectionId, gatewayEndpoint, advertisedAt);
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
