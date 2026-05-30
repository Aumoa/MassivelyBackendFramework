using System;

namespace PacketCore;

public static class PacketCodec
{
    public static PacketFrame Encode<TPacket>(
        PacketKind kind,
        ushort packetId,
        ushort version,
        TPacket value,
        IPacketCodec<TPacket> codec,
        PacketFlags flags = PacketFlags.None)
    {
        if (codec == null)
        {
            throw new ArgumentNullException(nameof(codec));
        }

        int payloadSize = codec.GetPayloadSize(value);
        var payload = OwnedPacketBuffer.Rent(payloadSize);

        try
        {
            var writer = new PacketWriter(payload.Span);
            codec.Encode(value, ref writer);
            if (writer.WrittenCount != payloadSize)
            {
                throw new InvalidOperationException("Codec wrote a different payload size than it declared.");
            }

            var header = new PacketHeader(kind, flags, packetId, version, payloadSize);
            var frame = new PacketFrame(header, payload);
            payload = null!;
            return frame;
        }
        finally
        {
            payload?.Dispose();
        }
    }

    public static TPacket Decode<TPacket>(
        PacketFrame frame,
        IPacketCodec<TPacket> codec,
        bool requireFullyConsumed = true)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (codec == null)
        {
            throw new ArgumentNullException(nameof(codec));
        }

        var reader = new PacketReader(frame.Payload.Span);
        var value = codec.Decode(ref reader);
        if (requireFullyConsumed && reader.Remaining != 0)
        {
            throw new PacketFormatException(PacketValidationError.TrailingPayload, "Packet payload has trailing bytes.");
        }

        return value;
    }
}
