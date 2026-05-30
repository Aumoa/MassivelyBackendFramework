using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class NodeHello
{
    public NodeHello(MasterNodeKind nodeKind, string nodeId, string displayName, ushort protocolVersion)
    {
        if (nodeKind is not (MasterNodeKind.Gateway or MasterNodeKind.Dedicated or MasterNodeKind.MasterAdmin))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeKind));
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node id is required.", nameof(nodeId));
        }

        if (displayName == null)
        {
            throw new ArgumentNullException(nameof(displayName));
        }

        if (protocolVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        }

        NodeKind = nodeKind;
        NodeId = nodeId;
        DisplayName = displayName;
        ProtocolVersion = protocolVersion;
    }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public ushort ProtocolVersion { get; }

    public static IPacketCodec<NodeHello> Codec { get; } = new NodeHelloCodec();

    private sealed class NodeHelloCodec : IPacketCodec<NodeHello>
    {
        public int GetPayloadSize(NodeHello value)
        {
            return sizeof(byte) + sizeof(ushort) + PacketWriter.GetStringSize(value.NodeId) + PacketWriter.GetStringSize(value.DisplayName);
        }

        public void Encode(NodeHello value, ref PacketWriter writer)
        {
            writer.WriteByte((byte)value.NodeKind);
            writer.WriteUInt16(value.ProtocolVersion);
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
        }

        public NodeHello Decode(ref PacketReader reader)
        {
            var nodeKind = (MasterNodeKind)reader.ReadByte();
            ushort protocolVersion = reader.ReadUInt16();
            string nodeId = reader.ReadString();
            string displayName = reader.ReadString();

            return new NodeHello(nodeKind, nodeId, displayName, protocolVersion);
        }
    }
}
