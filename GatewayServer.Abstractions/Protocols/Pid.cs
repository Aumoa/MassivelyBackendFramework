using PacketCore;

namespace GatewayServer.Protocols;

public static class Pid
{
    public const int PROTOCOL_TYPE_BITS = Packet.PROTOCOL_TYPE_BITS;
    public const int PROTOCOL_RESERVED_BITS = Packet.PROTOCOL_RESERVED_BITS;
    public const int PROTOCOL_ID_BITS = Packet.PROTOCOL_ID_BITS;
    public const int PROTOCOL_VERSION_BITS = Packet.PROTOCOL_VERSION_BITS;
    public const int PROTOCOL_PAYLOAD_SIZE_BITS = Packet.PROTOCOL_PAYLOAD_SIZE_BITS;
    public const int PROTOCOL_HEADER_SIZE_IN_BYTES = Packet.PROTOCOL_HEADER_SIZE_IN_BYTES;

    // REQUEST: 4XXX  (0b01 + code)
    // RESPONSE: 8XXX (0b10 + code)
    // NOTIFY: CXXX (0b11 + code)

    public const int GATE_HANDSHAKE_ECHO_REQ = 0xC001;
    public const int GATE_HANDSHAKE_ECHO_ACK = 0x8001;
    public const int GATE_HANDSHAKE_ECHO_NFY = 0xC001;
}
