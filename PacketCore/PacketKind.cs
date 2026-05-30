namespace PacketCore;

public enum PacketKind : byte
{
    Request = 0,
    Response = 1,
    Notify = 2,
    Control = 3
}
