namespace PacketCore;

public interface IPacketCodec<TPacket>
{
    int GetPayloadSize(TPacket value);

    void Encode(TPacket value, ref PacketWriter writer);

    TPacket Decode(ref PacketReader reader);
}
