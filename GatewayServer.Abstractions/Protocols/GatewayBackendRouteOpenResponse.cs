using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteOpenResponse
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendRouteOpenResponse(
        GatewayBackendRouteToken? routeToken,
        string backendKind,
        bool success,
        string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        if (success && routeToken == null)
        {
            throw new ArgumentException("Successful route open responses require a route token.", nameof(routeToken));
        }

        if (!success && routeToken != null)
        {
            throw new ArgumentException("Rejected route open responses must not include a route token.", nameof(routeToken));
        }

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Rejected route open responses require an error message.", nameof(errorMessage));
        }

        RouteToken = routeToken;
        BackendKind = backendKind.Trim();
        Success = success;
        ErrorMessage = errorMessage;
    }

    public GatewayBackendRouteToken? RouteToken { get; }

    public string BackendKind { get; }

    public bool Success { get; }

    public string ErrorMessage { get; }

    public static GatewayBackendRouteOpenResponse Accepted(
        GatewayBackendRouteToken routeToken,
        string backendKind)
    {
        return new GatewayBackendRouteOpenResponse(routeToken, backendKind, true, string.Empty);
    }

    public static GatewayBackendRouteOpenResponse Rejected(
        string backendKind,
        string errorMessage)
    {
        return new GatewayBackendRouteOpenResponse(null, backendKind, false, errorMessage);
    }

    public static IPacketCodec<GatewayBackendRouteOpenResponse> Codec { get; } = new GatewayBackendRouteOpenResponseCodec();

    private sealed class GatewayBackendRouteOpenResponseCodec : IPacketCodec<GatewayBackendRouteOpenResponse>
    {
        public int GetPayloadSize(GatewayBackendRouteOpenResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var routeTokenSize = value.RouteToken == null
                ? 0
                : PacketWriter.GetStringSize(value.RouteToken.Value);
            return sizeof(byte) +
                   PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(byte) +
                   routeTokenSize +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(GatewayBackendRouteOpenResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.BackendKind);
            if (value.RouteToken == null)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteString(value.RouteToken.Value);
            }

            writer.WriteString(value.ErrorMessage);
        }

        public GatewayBackendRouteOpenResponse Decode(ref PacketReader reader)
        {
            var success = reader.ReadByte() != 0;
            var backendKind = reader.ReadString();
            var hasRouteToken = reader.ReadByte() != 0;
            GatewayBackendRouteToken? routeToken = hasRouteToken
                ? new GatewayBackendRouteToken(reader.ReadString())
                : null;
            var errorMessage = reader.ReadString();
            return new GatewayBackendRouteOpenResponse(routeToken, backendKind, success, errorMessage);
        }
    }
}
