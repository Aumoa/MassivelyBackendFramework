using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendChannelDataEnvelope
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendChannelDataEnvelope(
        uint channelId,
        PacketKind routedKind,
        ushort routedPacketId,
        ushort routedVersion,
        GatewayBackendExchangeId? exchangeId,
        byte[] routedPayload)
    {
        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        if (routedKind is not (PacketKind.Request or PacketKind.Response or PacketKind.Notify))
        {
            throw new ArgumentOutOfRangeException(nameof(routedKind));
        }

        if (routedPacketId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedPacketId));
        }

        if (routedVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedVersion));
        }

        if ((routedKind is PacketKind.Request or PacketKind.Response) &&
            exchangeId == null)
        {
            throw new ArgumentException("Request and response Backend channel data frames require an exchange id.", nameof(exchangeId));
        }

        if (routedPayload == null)
        {
            throw new ArgumentNullException(nameof(routedPayload));
        }

        if (routedPayload.Length > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(routedPayload));
        }

        ChannelId = channelId;
        RoutedKind = routedKind;
        RoutedPacketId = routedPacketId;
        RoutedVersion = routedVersion;
        ExchangeId = exchangeId;
        RoutedPayload = routedPayload;
    }

    public uint ChannelId { get; }

    public PacketKind RoutedKind { get; }

    public ushort RoutedPacketId { get; }

    public ushort RoutedVersion { get; }

    public GatewayBackendExchangeId? ExchangeId { get; }

    public byte[] RoutedPayload { get; }

    public PacketFrame CreateRoutedFrame()
    {
        return PacketFrame.Create(
            RoutedKind,
            RoutedPacketId,
            RoutedVersion,
            RoutedPayload);
    }

    public static IPacketCodec<GatewayBackendChannelDataEnvelope> Codec { get; } = new GatewayBackendChannelDataEnvelopeCodec();

    private sealed class GatewayBackendChannelDataEnvelopeCodec : IPacketCodec<GatewayBackendChannelDataEnvelope>
    {
        public int GetPayloadSize(GatewayBackendChannelDataEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return sizeof(uint) +
                   sizeof(byte) +
                   sizeof(ushort) +
                   sizeof(ushort) +
                   sizeof(byte) +
                   (value.ExchangeId == null ? 0 : 16) +
                   sizeof(int) +
                   value.RoutedPayload.Length;
        }

        public void Encode(GatewayBackendChannelDataEnvelope value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteUInt32(value.ChannelId);
            writer.WriteByte((byte)value.RoutedKind);
            writer.WriteUInt16(value.RoutedPacketId);
            writer.WriteUInt16(value.RoutedVersion);
            if (value.ExchangeId == null)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteGuid(value.ExchangeId.Value.Value);
            }

            writer.WriteInt32(value.RoutedPayload.Length);
            writer.WriteBytes(value.RoutedPayload);
        }

        public GatewayBackendChannelDataEnvelope Decode(ref PacketReader reader)
        {
            var channelId = reader.ReadUInt32();
            var routedKind = (PacketKind)reader.ReadByte();
            var routedPacketId = reader.ReadUInt16();
            var routedVersion = reader.ReadUInt16();
            var hasExchangeId = reader.ReadByte() != 0;
            GatewayBackendExchangeId? exchangeId = hasExchangeId
                ? new GatewayBackendExchangeId(reader.ReadGuid())
                : null;
            var routedPayloadLength = reader.ReadInt32();
            if (routedPayloadLength < 0)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid routed payload length.");
            }

            byte[] routedPayload = reader.ReadBytes(routedPayloadLength).ToArray();
            return new GatewayBackendChannelDataEnvelope(
                channelId,
                routedKind,
                routedPacketId,
                routedVersion,
                exchangeId,
                routedPayload);
        }
    }
}
