using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class NodeAuthChallenge
{
    public NodeAuthChallenge(string challengeId, byte[] nonce)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            throw new ArgumentException("Challenge id is required.", nameof(challengeId));
        }

        if (nonce == null)
        {
            throw new ArgumentNullException(nameof(nonce));
        }

        if (nonce.Length != MasterControlProtocol.AuthNonceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(nonce));
        }

        ChallengeId = challengeId;
        m_Nonce = new byte[nonce.Length];
        Array.Copy(nonce, m_Nonce, nonce.Length);
    }

    private readonly byte[] m_Nonce;

    public string ChallengeId { get; }

    public ReadOnlyMemory<byte> Nonce => m_Nonce;

    public static IPacketCodec<NodeAuthChallenge> Codec { get; } = new NodeAuthChallengeCodec();

    private sealed class NodeAuthChallengeCodec : IPacketCodec<NodeAuthChallenge>
    {
        public int GetPayloadSize(NodeAuthChallenge value)
        {
            return PacketWriter.GetStringSize(value.ChallengeId) + sizeof(int) + value.Nonce.Length;
        }

        public void Encode(NodeAuthChallenge value, ref PacketWriter writer)
        {
            writer.WriteString(value.ChallengeId);
            writer.WriteInt32(value.Nonce.Length);
            writer.WriteBytes(value.Nonce.Span);
        }

        public NodeAuthChallenge Decode(ref PacketReader reader)
        {
            string challengeId = reader.ReadString();
            int nonceLength = reader.ReadInt32();
            if (nonceLength != MasterControlProtocol.AuthNonceLength)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid node authentication nonce length.");
            }

            return new NodeAuthChallenge(challengeId, reader.ReadBytes(nonceLength).ToArray());
        }
    }
}
