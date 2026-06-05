using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugAuthChallenge
{
    public RemoteDebugAuthChallenge(string challengeId, byte[] nonce)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            throw new ArgumentException("Challenge id is required.", nameof(challengeId));
        }

        if (nonce == null)
        {
            throw new ArgumentNullException(nameof(nonce));
        }

        if (nonce.Length != RemoteDebugProtocol.AuthNonceLength)
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

    public static IPacketCodec<RemoteDebugAuthChallenge> Codec { get; } = new RemoteDebugAuthChallengeCodec();

    private sealed class RemoteDebugAuthChallengeCodec : IPacketCodec<RemoteDebugAuthChallenge>
    {
        public int GetPayloadSize(RemoteDebugAuthChallenge value)
        {
            return PacketWriter.GetStringSize(value.ChallengeId) + sizeof(int) + value.Nonce.Length;
        }

        public void Encode(RemoteDebugAuthChallenge value, ref PacketWriter writer)
        {
            writer.WriteString(value.ChallengeId);
            writer.WriteInt32(value.Nonce.Length);
            writer.WriteBytes(value.Nonce.Span);
        }

        public RemoteDebugAuthChallenge Decode(ref PacketReader reader)
        {
            string challengeId = reader.ReadString();
            int nonceLength = reader.ReadInt32();
            if (nonceLength != RemoteDebugProtocol.AuthNonceLength)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid RemoteDebug authentication nonce length.");
            }

            return new RemoteDebugAuthChallenge(challengeId, reader.ReadBytes(nonceLength).ToArray());
        }
    }
}
