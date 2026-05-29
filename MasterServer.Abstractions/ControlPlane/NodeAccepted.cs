using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class NodeAccepted
{
    public NodeAccepted(string nodeId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node id is required.", nameof(nodeId));
        }

        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection id is required.", nameof(connectionId));
        }

        NodeId = nodeId;
        ConnectionId = connectionId;
    }

    public string NodeId { get; }

    public string ConnectionId { get; }

    public static IPacketCodec<NodeAccepted> Codec { get; } = new NodeAcceptedCodec();

    private sealed class NodeAcceptedCodec : IPacketCodec<NodeAccepted>
    {
        public int GetPayloadSize(NodeAccepted value)
        {
            return PacketWriter.GetStringSize(value.NodeId) + PacketWriter.GetStringSize(value.ConnectionId);
        }

        public void Encode(NodeAccepted value, ref PacketWriter writer)
        {
            writer.WriteString(value.NodeId);
            writer.WriteString(value.ConnectionId);
        }

        public NodeAccepted Decode(ref PacketReader reader)
        {
            return new NodeAccepted(reader.ReadString(), reader.ReadString());
        }
    }
}
