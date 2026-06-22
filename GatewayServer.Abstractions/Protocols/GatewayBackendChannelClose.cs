using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendChannelClose
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendChannelClose(
        uint channelId,
        string reason)
    {
        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        ChannelId = channelId;
        Reason = reason;
    }

    public uint ChannelId { get; }

    public string Reason { get; }

    public static IPacketCodec<GatewayBackendChannelClose> Codec { get; } = new GatewayBackendChannelCloseCodec();

    private sealed class GatewayBackendChannelCloseCodec : IPacketCodec<GatewayBackendChannelClose>
    {
        public int GetPayloadSize(GatewayBackendChannelClose value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return sizeof(uint) +
                   PacketWriter.GetStringSize(value.Reason);
        }

        public void Encode(GatewayBackendChannelClose value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteUInt32(value.ChannelId);
            writer.WriteString(value.Reason);
        }

        public GatewayBackendChannelClose Decode(ref PacketReader reader)
        {
            return new GatewayBackendChannelClose(
                reader.ReadUInt32(),
                reader.ReadString());
        }
    }
}
