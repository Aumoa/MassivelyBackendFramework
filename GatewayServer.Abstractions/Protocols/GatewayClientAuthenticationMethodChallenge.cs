using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayClientAuthenticationMethodChallenge
{
    public GatewayClientAuthenticationMethodChallenge(
        string methodId,
        GatewayClientAuthenticationMethodKind kind,
        string displayName,
        string loginUri,
        string completionToken,
        DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(methodId))
        {
            throw new ArgumentException("Authentication method id is required.", nameof(methodId));
        }

        if (!Enum.IsDefined(typeof(GatewayClientAuthenticationMethodKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Authentication method display name is required.", nameof(displayName));
        }

        MethodId = methodId.Trim();
        Kind = kind;
        DisplayName = displayName.Trim();
        LoginUri = loginUri?.Trim() ?? string.Empty;
        CompletionToken = completionToken?.Trim() ?? string.Empty;
        ExpiresAt = expiresAt;
    }

    public string MethodId { get; }

    public GatewayClientAuthenticationMethodKind Kind { get; }

    public string DisplayName { get; }

    public string LoginUri { get; }

    public string CompletionToken { get; }

    public DateTimeOffset ExpiresAt { get; }

    public static IPacketCodec<GatewayClientAuthenticationMethodChallenge> Codec { get; } = new GatewayClientAuthenticationMethodChallengeCodec();

    private sealed class GatewayClientAuthenticationMethodChallengeCodec : IPacketCodec<GatewayClientAuthenticationMethodChallenge>
    {
        public int GetPayloadSize(GatewayClientAuthenticationMethodChallenge value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.MethodId) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.LoginUri) +
                   PacketWriter.GetStringSize(value.CompletionToken) +
                   sizeof(long);
        }

        public void Encode(GatewayClientAuthenticationMethodChallenge value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.MethodId);
            writer.WriteByte((byte)value.Kind);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.LoginUri);
            writer.WriteString(value.CompletionToken);
            writer.WriteInt64(value.ExpiresAt.ToUnixTimeMilliseconds());
        }

        public GatewayClientAuthenticationMethodChallenge Decode(ref PacketReader reader)
        {
            return new GatewayClientAuthenticationMethodChallenge(
                reader.ReadString(),
                (GatewayClientAuthenticationMethodKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()));
        }
    }
}
