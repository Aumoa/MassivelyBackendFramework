using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteEnvelope
{
    public const ushort ProtocolVersion = 1;

    public GatewayBackendRouteEnvelope(
        string backendKind,
        Guid routeId,
        PacketKind routedKind,
        ushort routedPacketId,
        ushort routedVersion,
        byte[] routedPayload)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (routeId == Guid.Empty)
        {
            throw new ArgumentException("Route id is required.", nameof(routeId));
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

        if (routedPayload == null)
        {
            throw new ArgumentNullException(nameof(routedPayload));
        }

        if (routedPayload.Length > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(routedPayload));
        }

        BackendKind = backendKind.Trim();
        RouteId = routeId;
        RoutedKind = routedKind;
        RoutedPacketId = routedPacketId;
        RoutedVersion = routedVersion;
        RoutedPayload = routedPayload;
    }

    public string BackendKind { get; }

    public Guid RouteId { get; }

    public PacketKind RoutedKind { get; }

    public ushort RoutedPacketId { get; }

    public ushort RoutedVersion { get; }

    public byte[] RoutedPayload { get; }

    public PacketFrame CreateRoutedFrame()
    {
        return PacketFrame.Create(
            RoutedKind,
            RoutedPacketId,
            RoutedVersion,
            RoutedPayload);
    }

    public static IPacketCodec<GatewayBackendRouteEnvelope> Codec { get; } = new GatewayBackendRouteEnvelopeCodec();

    private sealed class GatewayBackendRouteEnvelopeCodec : IPacketCodec<GatewayBackendRouteEnvelope>
    {
        public int GetPayloadSize(GatewayBackendRouteEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind) +
                   16 +
                   sizeof(byte) +
                   sizeof(ushort) +
                   sizeof(ushort) +
                   sizeof(int) +
                   value.RoutedPayload.Length;
        }

        public void Encode(GatewayBackendRouteEnvelope value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            writer.WriteGuid(value.RouteId);
            writer.WriteByte((byte)value.RoutedKind);
            writer.WriteUInt16(value.RoutedPacketId);
            writer.WriteUInt16(value.RoutedVersion);
            writer.WriteInt32(value.RoutedPayload.Length);
            writer.WriteBytes(value.RoutedPayload);
        }

        public GatewayBackendRouteEnvelope Decode(ref PacketReader reader)
        {
            string backendKind = reader.ReadString();
            Guid routeId = reader.ReadGuid();
            var routedKind = (PacketKind)reader.ReadByte();
            ushort routedPacketId = reader.ReadUInt16();
            ushort routedVersion = reader.ReadUInt16();
            int routedPayloadLength = reader.ReadInt32();
            if (routedPayloadLength < 0)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid routed payload length.");
            }

            byte[] routedPayload = reader.ReadBytes(routedPayloadLength).ToArray();
            return new GatewayBackendRouteEnvelope(
                backendKind,
                routeId,
                routedKind,
                routedPacketId,
                routedVersion,
                routedPayload);
        }
    }
}
