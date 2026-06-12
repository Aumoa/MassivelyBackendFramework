using PacketCore;

namespace GatewayServer.Protocols;

public static class Pid
{
    public const int PROTOCOL_HEADER_SIZE_IN_BYTES = PacketHeader.Size;

    public const ushort GATE_HANDSHAKE_NOTIFY = 1;
    public const ushort GATE_HANDSHAKE_ECHO = 2;
    public const ushort GATE_BACKEND_ROUTE = 3;
}
