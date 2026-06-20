using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteClose
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendRouteClose(
        GatewayBackendRouteToken routeToken,
        string reason)
    {
        if (routeToken == null)
        {
            throw new ArgumentNullException(nameof(routeToken));
        }

        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        RouteToken = routeToken;
        Reason = reason;
    }

    public GatewayBackendRouteToken RouteToken { get; }

    public string Reason { get; }

    public static IPacketCodec<GatewayBackendRouteClose> Codec { get; } = new GatewayBackendRouteCloseCodec();

    private sealed class GatewayBackendRouteCloseCodec : IPacketCodec<GatewayBackendRouteClose>
    {
        public int GetPayloadSize(GatewayBackendRouteClose value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.RouteToken.Value) +
                   PacketWriter.GetStringSize(value.Reason);
        }

        public void Encode(GatewayBackendRouteClose value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.RouteToken.Value);
            writer.WriteString(value.Reason);
        }

        public GatewayBackendRouteClose Decode(ref PacketReader reader)
        {
            return new GatewayBackendRouteClose(
                new GatewayBackendRouteToken(reader.ReadString()),
                reader.ReadString());
        }
    }
}
