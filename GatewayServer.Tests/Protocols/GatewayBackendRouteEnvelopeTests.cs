using GatewayServer.Protocols;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class GatewayBackendRouteEnvelopeTests
{
    [Fact]
    public void Codec_RoundTrips_RoutedPacketMetadata()
    {
        var routeId = Guid.NewGuid();
        byte[] payload = [1, 2, 3, 4];
        var envelope = new GatewayBackendRouteEnvelope(
            " backend-kind ",
            routeId,
            PacketKind.Request,
            routedPacketId: 123,
            routedVersion: 2,
            payload);

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteEnvelope.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec);

        Assert.Equal("backend-kind", decoded.BackendKind);
        Assert.Equal(routeId, decoded.RouteId);
        Assert.Equal(PacketKind.Request, decoded.RoutedKind);
        Assert.Equal((ushort)123, decoded.RoutedPacketId);
        Assert.Equal((ushort)2, decoded.RoutedVersion);
        Assert.Equal(payload, decoded.RoutedPayload);
    }

    [Fact]
    public void Constructor_Rejects_ControlPackets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayBackendRouteEnvelope(
            "backend-kind",
            Guid.NewGuid(),
            PacketKind.Control,
            routedPacketId: 123,
            routedVersion: 1,
            []));
    }

    [Fact]
    public void Constructor_Rejects_EmptyRouteId()
    {
        Assert.Throws<ArgumentException>(() => new GatewayBackendRouteEnvelope(
            "backend-kind",
            Guid.Empty,
            PacketKind.Request,
            routedPacketId: 123,
            routedVersion: 1,
            []));
    }
}
