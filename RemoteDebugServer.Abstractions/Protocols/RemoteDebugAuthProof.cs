using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugAuthProof
{
    public const int ProofLength = 32;

    public RemoteDebugAuthProof(string clientId, byte[] proof)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (proof == null)
        {
            throw new ArgumentNullException(nameof(proof));
        }

        if (proof.Length != ProofLength)
        {
            throw new ArgumentOutOfRangeException(nameof(proof));
        }

        ClientId = clientId;
        m_Proof = new byte[proof.Length];
        Array.Copy(proof, m_Proof, proof.Length);
    }

    private readonly byte[] m_Proof;

    public string ClientId { get; }

    public ReadOnlyMemory<byte> Proof => m_Proof;

    public static IPacketCodec<RemoteDebugAuthProof> Codec { get; } = new RemoteDebugAuthProofCodec();

    private sealed class RemoteDebugAuthProofCodec : IPacketCodec<RemoteDebugAuthProof>
    {
        public int GetPayloadSize(RemoteDebugAuthProof value)
        {
            return PacketWriter.GetStringSize(value.ClientId) + sizeof(int) + value.Proof.Length;
        }

        public void Encode(RemoteDebugAuthProof value, ref PacketWriter writer)
        {
            writer.WriteString(value.ClientId);
            writer.WriteInt32(value.Proof.Length);
            writer.WriteBytes(value.Proof.Span);
        }

        public RemoteDebugAuthProof Decode(ref PacketReader reader)
        {
            string clientId = reader.ReadString();
            int proofLength = reader.ReadInt32();
            if (proofLength != ProofLength)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid RemoteDebug authentication proof length.");
            }

            return new RemoteDebugAuthProof(clientId, reader.ReadBytes(proofLength).ToArray());
        }
    }
}
