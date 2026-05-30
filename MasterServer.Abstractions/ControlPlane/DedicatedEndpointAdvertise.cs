using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DedicatedEndpointAdvertise
{
    public DedicatedEndpointAdvertise(MasterSocketEndpoint gatewayEndpoint)
    {
        GatewayEndpoint = gatewayEndpoint ?? throw new ArgumentNullException(nameof(gatewayEndpoint));
    }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public static IPacketCodec<DedicatedEndpointAdvertise> Codec { get; } = new DedicatedEndpointAdvertiseCodec();

    private sealed class DedicatedEndpointAdvertiseCodec : IPacketCodec<DedicatedEndpointAdvertise>
    {
        public int GetPayloadSize(DedicatedEndpointAdvertise value)
        {
            return GetEndpointSize(value.GatewayEndpoint);
        }

        public void Encode(DedicatedEndpointAdvertise value, ref PacketWriter writer)
        {
            WriteEndpoint(value.GatewayEndpoint, ref writer);
        }

        public DedicatedEndpointAdvertise Decode(ref PacketReader reader)
        {
            return new DedicatedEndpointAdvertise(ReadEndpoint(ref reader));
        }

        private static int GetEndpointSize(MasterSocketEndpoint value)
        {
            return PacketWriter.GetStringSize(value.IPAddress) + sizeof(int) + sizeof(byte);
        }

        private static void WriteEndpoint(MasterSocketEndpoint value, ref PacketWriter writer)
        {
            writer.WriteString(value.IPAddress);
            writer.WriteInt32(value.Port);
            writer.WriteByte(value.UseTls ? (byte)1 : (byte)0);
        }

        private static MasterSocketEndpoint ReadEndpoint(ref PacketReader reader)
        {
            string ipAddress = reader.ReadString();
            int port = reader.ReadInt32();
            bool useTls = reader.ReadByte() != 0;
            return new MasterSocketEndpoint(ipAddress, port, useTls);
        }
    }
}
