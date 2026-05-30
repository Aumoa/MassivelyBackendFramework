using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class NodeAuthProof
{
    public const int ProofLength = 32;

    public NodeAuthProof(string nodeId, byte[] proof)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node id is required.", nameof(nodeId));
        }

        if (proof == null)
        {
            throw new ArgumentNullException(nameof(proof));
        }

        if (proof.Length != ProofLength)
        {
            throw new ArgumentOutOfRangeException(nameof(proof));
        }

        NodeId = nodeId;
        m_Proof = new byte[proof.Length];
        Array.Copy(proof, m_Proof, proof.Length);
    }

    private readonly byte[] m_Proof;

    public string NodeId { get; }

    public ReadOnlyMemory<byte> Proof => m_Proof;

    public static IPacketCodec<NodeAuthProof> Codec { get; } = new NodeAuthProofCodec();

    private sealed class NodeAuthProofCodec : IPacketCodec<NodeAuthProof>
    {
        public int GetPayloadSize(NodeAuthProof value)
        {
            return PacketWriter.GetStringSize(value.NodeId) + sizeof(int) + value.Proof.Length;
        }

        public void Encode(NodeAuthProof value, ref PacketWriter writer)
        {
            writer.WriteString(value.NodeId);
            writer.WriteInt32(value.Proof.Length);
            writer.WriteBytes(value.Proof.Span);
        }

        public NodeAuthProof Decode(ref PacketReader reader)
        {
            string nodeId = reader.ReadString();
            int proofLength = reader.ReadInt32();
            if (proofLength != ProofLength)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid node authentication proof length.");
            }

            return new NodeAuthProof(nodeId, reader.ReadBytes(proofLength).ToArray());
        }
    }
}
