using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendChannelOpen
{
    public const ushort ProtocolVersion = 2;

    public GatewayBackendChannelOpen(
        uint channelId,
        string? principalSubjectId)
        : this(channelId, principalSubjectId, null, null)
    {
    }

    public GatewayBackendChannelOpen(
        uint channelId,
        string? principalSubjectId,
        GatewayClientAuthenticationMethodKind? principalAuthenticationMethodKind,
        string? principalAuthenticationMethodId)
    {
        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (principalSubjectId != null && string.IsNullOrWhiteSpace(principalSubjectId))
        {
            throw new ArgumentException("Principal subject id cannot be blank.", nameof(principalSubjectId));
        }

        if (principalAuthenticationMethodKind.HasValue &&
            !Enum.IsDefined(typeof(GatewayClientAuthenticationMethodKind), principalAuthenticationMethodKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(principalAuthenticationMethodKind));
        }

        if (principalAuthenticationMethodId != null && string.IsNullOrWhiteSpace(principalAuthenticationMethodId))
        {
            throw new ArgumentException("Principal authentication method id cannot be blank.", nameof(principalAuthenticationMethodId));
        }

        if (principalAuthenticationMethodKind.HasValue != (principalAuthenticationMethodId != null))
        {
            throw new ArgumentException("Principal authentication method kind and id must be provided together.");
        }

        if (principalSubjectId == null &&
            (principalAuthenticationMethodKind.HasValue || principalAuthenticationMethodId != null))
        {
            throw new ArgumentException("Principal authentication method cannot be provided without a principal subject id.");
        }

        ChannelId = channelId;
        PrincipalSubjectId = principalSubjectId;
        PrincipalAuthenticationMethodKind = principalAuthenticationMethodKind;
        PrincipalAuthenticationMethodId = principalAuthenticationMethodId;
    }

    public uint ChannelId { get; }

    public string? PrincipalSubjectId { get; }

    public GatewayClientAuthenticationMethodKind? PrincipalAuthenticationMethodKind { get; }

    public string? PrincipalAuthenticationMethodId { get; }

    public static IPacketCodec<GatewayBackendChannelOpen> Codec { get; } = new GatewayBackendChannelOpenCodec();

    private sealed class GatewayBackendChannelOpenCodec : IPacketCodec<GatewayBackendChannelOpen>
    {
        public int GetPayloadSize(GatewayBackendChannelOpen value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return sizeof(uint) +
                   sizeof(byte) +
                   (value.PrincipalSubjectId == null
                       ? 0
                       : PacketWriter.GetStringSize(value.PrincipalSubjectId)) +
                   sizeof(byte) +
                   (value.PrincipalAuthenticationMethodKind.HasValue
                       ? sizeof(byte) + PacketWriter.GetStringSize(value.PrincipalAuthenticationMethodId!)
                       : 0);
        }

        public void Encode(GatewayBackendChannelOpen value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteUInt32(value.ChannelId);
            if (value.PrincipalSubjectId == null)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteString(value.PrincipalSubjectId);
            }

            if (!value.PrincipalAuthenticationMethodKind.HasValue)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteByte((byte)value.PrincipalAuthenticationMethodKind.Value);
                writer.WriteString(value.PrincipalAuthenticationMethodId!);
            }
        }

        public GatewayBackendChannelOpen Decode(ref PacketReader reader)
        {
            var channelId = reader.ReadUInt32();
            var hasPrincipalSubjectId = reader.ReadByte() != 0;
            var principalSubjectId = hasPrincipalSubjectId ? reader.ReadString() : null;
            var hasPrincipalAuthenticationMethod = reader.ReadByte() != 0;
            GatewayClientAuthenticationMethodKind? principalAuthenticationMethodKind = null;
            string? principalAuthenticationMethodId = null;
            if (hasPrincipalAuthenticationMethod)
            {
                principalAuthenticationMethodKind = (GatewayClientAuthenticationMethodKind)reader.ReadByte();
                principalAuthenticationMethodId = reader.ReadString();
            }

            return new GatewayBackendChannelOpen(
                channelId,
                principalSubjectId,
                principalAuthenticationMethodKind,
                principalAuthenticationMethodId);
        }
    }
}
