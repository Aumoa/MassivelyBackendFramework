using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MasterServer.ControlPlane;

public static class MasterNodeAuthenticator
{
    public static byte[] ComputeProof(NodeAuthChallenge challenge, NodeHello hello, string sharedSecret)
    {
        if (challenge == null)
        {
            throw new ArgumentNullException(nameof(challenge));
        }

        if (hello == null)
        {
            throw new ArgumentNullException(nameof(hello));
        }

        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            throw new ArgumentException("Shared secret is required.", nameof(sharedSecret));
        }

        byte[] key = Encoding.UTF8.GetBytes(sharedSecret);
        using var hmac = new HMACSHA256(key);
        using var payload = new MemoryStream();

        WriteString(payload, challenge.ChallengeId);
        WriteBytes(payload, challenge.Nonce.ToArray());
        payload.WriteByte((byte)hello.NodeKind);
        WriteUInt16(payload, hello.ProtocolVersion);
        WriteString(payload, hello.NodeId);
        WriteString(payload, hello.DisplayName);

        return hmac.ComputeHash(payload.ToArray());
    }

    public static bool VerifyProof(NodeAuthChallenge challenge, NodeHello hello, NodeAuthProof proof, string sharedSecret)
    {
        if (proof == null)
        {
            throw new ArgumentNullException(nameof(proof));
        }

        if (!string.Equals(hello.NodeId, proof.NodeId, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] expectedProof = ComputeProof(challenge, hello, sharedSecret);
        return FixedTimeEquals(expectedProof, proof.Proof.Span);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(value, 0, value.Length);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < left.Length; i++)
        {
            diff |= left[i] ^ right[i];
        }

        return diff == 0;
    }
}
