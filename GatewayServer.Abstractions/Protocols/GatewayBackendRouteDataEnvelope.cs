using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteDataEnvelope
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendRouteDataEnvelope(
        GatewayBackendRouteToken routeToken,
        GatewayBackendRouteDirection direction,
        PacketKind routedKind,
        ushort routedPacketId,
        ushort routedVersion,
        GatewayBackendExchangeId? exchangeId,
        byte[] routedPayload)
    {
        if (routeToken == null)
        {
            throw new ArgumentNullException(nameof(routeToken));
        }

        if (!IsValidDirection(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (routedKind is not (PacketKind.Request or PacketKind.Response or PacketKind.Notify))
        {
            throw new ArgumentOutOfRangeException(nameof(routedKind));
        }

        if (routedPacketId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedPacketId));
        }

        if (routedVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedVersion));
        }

        if ((routedKind is PacketKind.Request or PacketKind.Response) &&
            exchangeId == null)
        {
            throw new ArgumentException("Request and response route data frames require an exchange id.", nameof(exchangeId));
        }

        if (routedPayload == null)
        {
            throw new ArgumentNullException(nameof(routedPayload));
        }

        if (routedPayload.Length > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(routedPayload));
        }

        RouteToken = routeToken;
        Direction = direction;
        RoutedKind = routedKind;
        RoutedPacketId = routedPacketId;
        RoutedVersion = routedVersion;
        ExchangeId = exchangeId;
        RoutedPayload = routedPayload;
    }

    public GatewayBackendRouteToken RouteToken { get; }

    public GatewayBackendRouteDirection Direction { get; }

    public PacketKind RoutedKind { get; }

    public ushort RoutedPacketId { get; }

    public ushort RoutedVersion { get; }

    public GatewayBackendExchangeId? ExchangeId { get; }

    public byte[] RoutedPayload { get; }

    public PacketFrame CreateRoutedFrame()
    {
        return PacketFrame.Create(
            RoutedKind,
            RoutedPacketId,
            RoutedVersion,
            RoutedPayload);
    }

    public static IPacketCodec<GatewayBackendRouteDataEnvelope> Codec { get; } = new GatewayBackendRouteDataEnvelopeCodec();

    private static bool IsValidDirection(GatewayBackendRouteDirection direction)
    {
        return direction is GatewayBackendRouteDirection.ClientToBackend or GatewayBackendRouteDirection.BackendToClient;
    }

    private sealed class GatewayBackendRouteDataEnvelopeCodec : IPacketCodec<GatewayBackendRouteDataEnvelope>
    {
        public int GetPayloadSize(GatewayBackendRouteDataEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.RouteToken.Value) +
                   sizeof(byte) +
                   sizeof(byte) +
                   sizeof(ushort) +
                   sizeof(ushort) +
                   sizeof(byte) +
                   (value.ExchangeId == null ? 0 : 16) +
                   sizeof(int) +
                   value.RoutedPayload.Length;
        }

        public void Encode(GatewayBackendRouteDataEnvelope value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.RouteToken.Value);
            writer.WriteByte((byte)value.Direction);
            writer.WriteByte((byte)value.RoutedKind);
            writer.WriteUInt16(value.RoutedPacketId);
            writer.WriteUInt16(value.RoutedVersion);
            if (value.ExchangeId == null)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteGuid(value.ExchangeId.Value.Value);
            }

            writer.WriteInt32(value.RoutedPayload.Length);
            writer.WriteBytes(value.RoutedPayload);
        }

        public GatewayBackendRouteDataEnvelope Decode(ref PacketReader reader)
        {
            var routeToken = new GatewayBackendRouteToken(reader.ReadString());
            var direction = (GatewayBackendRouteDirection)reader.ReadByte();
            var routedKind = (PacketKind)reader.ReadByte();
            var routedPacketId = reader.ReadUInt16();
            var routedVersion = reader.ReadUInt16();
            var hasExchangeId = reader.ReadByte() != 0;
            GatewayBackendExchangeId? exchangeId = hasExchangeId
                ? new GatewayBackendExchangeId(reader.ReadGuid())
                : null;
            var routedPayloadLength = reader.ReadInt32();
            if (routedPayloadLength < 0)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid routed payload length.");
            }

            byte[] routedPayload = reader.ReadBytes(routedPayloadLength).ToArray();
            return new GatewayBackendRouteDataEnvelope(
                routeToken,
                direction,
                routedKind,
                routedPacketId,
                routedVersion,
                exchangeId,
                routedPayload);
        }
    }
}
