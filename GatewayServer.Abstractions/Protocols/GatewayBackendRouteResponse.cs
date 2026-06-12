using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteResponse
{
    public GatewayBackendRouteResponse(
        Guid routeId,
        string backendKind,
        bool success,
        string errorMessage)
    {
        if (routeId == Guid.Empty)
        {
            throw new ArgumentException("Route id is required.", nameof(routeId));
        }

        if (backendKind == null)
        {
            throw new ArgumentNullException(nameof(backendKind));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        RouteId = routeId;
        BackendKind = backendKind;
        Success = success;
        ErrorMessage = errorMessage;
    }

    public Guid RouteId { get; }

    public string BackendKind { get; }

    public bool Success { get; }

    public string ErrorMessage { get; }

    public static GatewayBackendRouteResponse Accepted(Guid routeId, string backendKind)
    {
        return new GatewayBackendRouteResponse(routeId, backendKind, true, string.Empty);
    }

    public static GatewayBackendRouteResponse Rejected(Guid routeId, string backendKind, string errorMessage)
    {
        return new GatewayBackendRouteResponse(routeId, backendKind, false, errorMessage);
    }

    public static IPacketCodec<GatewayBackendRouteResponse> Codec { get; } = new GatewayBackendRouteResponseCodec();

    private sealed class GatewayBackendRouteResponseCodec : IPacketCodec<GatewayBackendRouteResponse>
    {
        public int GetPayloadSize(GatewayBackendRouteResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind) +
                   16 +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(GatewayBackendRouteResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteGuid(value.RouteId);
            writer.WriteString(value.BackendKind);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.ErrorMessage);
        }

        public GatewayBackendRouteResponse Decode(ref PacketReader reader)
        {
            return new GatewayBackendRouteResponse(
                reader.ReadGuid(),
                reader.ReadString(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}
