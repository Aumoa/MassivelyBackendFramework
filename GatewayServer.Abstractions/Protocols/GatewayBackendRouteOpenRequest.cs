using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteOpenRequest
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendRouteOpenRequest(string backendKind)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        BackendKind = backendKind.Trim();
    }

    public string BackendKind { get; }

    public static IPacketCodec<GatewayBackendRouteOpenRequest> Codec { get; } = new GatewayBackendRouteOpenRequestCodec();

    private sealed class GatewayBackendRouteOpenRequestCodec : IPacketCodec<GatewayBackendRouteOpenRequest>
    {
        public int GetPayloadSize(GatewayBackendRouteOpenRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind);
        }

        public void Encode(GatewayBackendRouteOpenRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
        }

        public GatewayBackendRouteOpenRequest Decode(ref PacketReader reader)
        {
            return new GatewayBackendRouteOpenRequest(reader.ReadString());
        }
    }
}
