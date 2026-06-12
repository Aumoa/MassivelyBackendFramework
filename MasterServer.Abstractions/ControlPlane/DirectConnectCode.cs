using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCode
{
    public DirectConnectCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Direct connect code is required.", nameof(code));
        }

        Code = code;
    }

    public string Code { get; }

    public static IPacketCodec<DirectConnectCode> Codec { get; } = new DirectConnectCodeCodec();

    private sealed class DirectConnectCodeCodec : IPacketCodec<DirectConnectCode>
    {
        public int GetPayloadSize(DirectConnectCode value)
        {
            return PacketWriter.GetStringSize(value.Code);
        }

        public void Encode(DirectConnectCode value, ref PacketWriter writer)
        {
            writer.WriteString(value.Code);
        }

        public DirectConnectCode Decode(ref PacketReader reader)
        {
            return new DirectConnectCode(reader.ReadString());
        }
    }
}
