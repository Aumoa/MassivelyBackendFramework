using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayClientAuthenticationOptionsResponse
{
    private const int MaxChallengeCount = 32;

    public GatewayClientAuthenticationOptionsResponse(
        string backendKind,
        bool success,
        GatewayClientAuthenticationMethodChallenge[] challenges,
        string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (challenges == null)
        {
            throw new ArgumentNullException(nameof(challenges));
        }

        if (challenges.Length > MaxChallengeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(challenges));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        if (!success && challenges.Length != 0)
        {
            throw new ArgumentException("Rejected authentication options responses must not include challenges.", nameof(challenges));
        }

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Rejected authentication options responses require an error message.", nameof(errorMessage));
        }

        BackendKind = backendKind.Trim();
        Success = success;
        Challenges = challenges;
        ErrorMessage = errorMessage;
    }

    public string BackendKind { get; }

    public bool Success { get; }

    public GatewayClientAuthenticationMethodChallenge[] Challenges { get; }

    public string ErrorMessage { get; }

    public static GatewayClientAuthenticationOptionsResponse Accepted(
        string backendKind,
        GatewayClientAuthenticationMethodChallenge[] challenges)
    {
        return new GatewayClientAuthenticationOptionsResponse(
            backendKind,
            true,
            challenges,
            string.Empty);
    }

    public static GatewayClientAuthenticationOptionsResponse Rejected(
        string backendKind,
        string errorMessage)
    {
        return new GatewayClientAuthenticationOptionsResponse(
            backendKind,
            false,
            Array.Empty<GatewayClientAuthenticationMethodChallenge>(),
            errorMessage);
    }

    public static IPacketCodec<GatewayClientAuthenticationOptionsResponse> Codec { get; } = new GatewayClientAuthenticationOptionsResponseCodec();

    private sealed class GatewayClientAuthenticationOptionsResponseCodec : IPacketCodec<GatewayClientAuthenticationOptionsResponse>
    {
        public int GetPayloadSize(GatewayClientAuthenticationOptionsResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = PacketWriter.GetStringSize(value.BackendKind) +
                       sizeof(byte) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.ErrorMessage);
            foreach (var challenge in value.Challenges)
            {
                size += GatewayClientAuthenticationMethodChallenge.Codec.GetPayloadSize(challenge);
            }

            return size;
        }

        public void Encode(GatewayClientAuthenticationOptionsResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Challenges.Length);
            foreach (var challenge in value.Challenges)
            {
                GatewayClientAuthenticationMethodChallenge.Codec.Encode(challenge, ref writer);
            }

            writer.WriteString(value.ErrorMessage);
        }

        public GatewayClientAuthenticationOptionsResponse Decode(ref PacketReader reader)
        {
            var backendKind = reader.ReadString();
            var success = reader.ReadByte() != 0;
            var challengeCount = reader.ReadInt32();
            if (challengeCount < 0 || challengeCount > MaxChallengeCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway authentication challenge count.");
            }

            var challenges = new GatewayClientAuthenticationMethodChallenge[challengeCount];
            for (int i = 0; i < challenges.Length; i++)
            {
                challenges[i] = GatewayClientAuthenticationMethodChallenge.Codec.Decode(ref reader);
            }

            var errorMessage = reader.ReadString();
            return new GatewayClientAuthenticationOptionsResponse(
                backendKind,
                success,
                challenges,
                errorMessage);
        }
    }
}
