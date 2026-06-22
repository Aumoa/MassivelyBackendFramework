using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendChannelOpen
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendChannelOpen(
        uint channelId,
        string? principalSubjectId)
    {
        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (principalSubjectId != null && string.IsNullOrWhiteSpace(principalSubjectId))
        {
            throw new ArgumentException("Principal subject id cannot be blank.", nameof(principalSubjectId));
        }

        ChannelId = channelId;
        PrincipalSubjectId = principalSubjectId;
    }

    public uint ChannelId { get; }

    public string? PrincipalSubjectId { get; }

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
                       : PacketWriter.GetStringSize(value.PrincipalSubjectId));
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
        }

        public GatewayBackendChannelOpen Decode(ref PacketReader reader)
        {
            var channelId = reader.ReadUInt32();
            var hasPrincipalSubjectId = reader.ReadByte() != 0;
            return new GatewayBackendChannelOpen(
                channelId,
                hasPrincipalSubjectId ? reader.ReadString() : null);
        }
    }
}
