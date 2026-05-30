using System;

namespace PacketCore;

[Flags]
public enum PacketKindMask : byte
{
    None = 0,
    Request = 1 << PacketKind.Request,
    Response = 1 << PacketKind.Response,
    Notify = 1 << PacketKind.Notify,
    Control = 1 << PacketKind.Control,
    All = Request | Response | Notify | Control
}
