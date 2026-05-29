using PacketCore;

namespace GatewayServer.Protocols;

public readonly struct GatewayHandshakeNotify
{
    public GatewayHandshakeNotify(string loginUri)
    {
        LoginUri = loginUri;
    }

    public string LoginUri { get; }

    public static IPacketCodec<GatewayHandshakeNotify> Codec { get; } = new GatewayHandshakeNotifyCodec();

    private sealed class GatewayHandshakeNotifyCodec : IPacketCodec<GatewayHandshakeNotify>
    {
        public int GetPayloadSize(GatewayHandshakeNotify value)
        {
            return PacketWriter.GetStringSize(value.LoginUri);
        }

        public void Encode(GatewayHandshakeNotify value, ref PacketWriter writer)
        {
            writer.WriteString(value.LoginUri);
        }

        public GatewayHandshakeNotify Decode(ref PacketReader reader)
        {
            return new GatewayHandshakeNotify(reader.ReadString());
        }
    }
}
