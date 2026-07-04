using GatewayServer.Protocols;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class GatewayBackendPersistentRouteProtocolTests
{
    [Fact]
    public void OpenRequest_Codec_RoundTrips_BackendKind()
    {
        var request = new GatewayBackendRouteOpenRequest(" alpha ");

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenRequest.ProtocolVersion,
            request,
            GatewayBackendRouteOpenRequest.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenRequest.Codec);

        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Null(decoded.ServerHandle);
    }

    [Fact]
    public void OpenRequest_LegacyCodec_RoundTrips_BackendKind()
    {
        var request = new GatewayBackendRouteOpenRequest(" alpha ");

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenRequest.LegacyProtocolVersion,
            request,
            GatewayBackendRouteOpenRequest.LegacyCodec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenRequest.LegacyCodec);

        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Null(decoded.ServerHandle);
    }

    [Fact]
    public void OpenRequest_Codec_RoundTrips_ServerHandle()
    {
        var serverHandle = new GatewayBackendServerHandle("server-alpha");
        var request = new GatewayBackendRouteOpenRequest(" alpha ", serverHandle);

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenRequest.ProtocolVersion,
            request,
            GatewayBackendRouteOpenRequest.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenRequest.Codec);

        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Equal(serverHandle, decoded.ServerHandle);
    }

    [Fact]
    public void OpenResponse_Codec_RoundTrips_AcceptedToken()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        var response = GatewayBackendRouteOpenResponse.Accepted(routeToken, " alpha ");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenResponse.ProtocolVersion,
            response,
            GatewayBackendRouteOpenResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenResponse.Codec);

        Assert.True(decoded.Success);
        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal(string.Empty, decoded.ErrorMessage);
        Assert.Null(decoded.ServerHandle);
        Assert.Equal(string.Empty, decoded.DescriptorVersion);
        Assert.Equal(string.Empty, decoded.DescriptorHash);
    }

    [Fact]
    public void OpenResponse_LegacyCodec_RoundTrips_CommonFields()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        var response = GatewayBackendRouteOpenResponse.Accepted(
            routeToken,
            " alpha ",
            new GatewayBackendServerHandle("server-alpha"),
            "v1",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenResponse.LegacyProtocolVersion,
            response,
            GatewayBackendRouteOpenResponse.LegacyCodec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenResponse.LegacyCodec);

        Assert.True(decoded.Success);
        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal(string.Empty, decoded.ErrorMessage);
        Assert.Null(decoded.ServerHandle);
        Assert.Equal(string.Empty, decoded.DescriptorVersion);
        Assert.Equal(string.Empty, decoded.DescriptorHash);
    }

    [Fact]
    public void OpenResponse_Codec_RoundTrips_SelectedServerMetadata()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        var serverHandle = new GatewayBackendServerHandle("server-alpha");
        var response = GatewayBackendRouteOpenResponse.Accepted(
            routeToken,
            " alpha ",
            serverHandle,
            "v1",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenResponse.ProtocolVersion,
            response,
            GatewayBackendRouteOpenResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteOpenResponse.Codec);

        Assert.True(decoded.Success);
        Assert.Equal("alpha", decoded.BackendKind);
        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal(serverHandle, decoded.ServerHandle);
        Assert.Equal("v1", decoded.DescriptorVersion);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", decoded.DescriptorHash);
    }

    [Fact]
    public void ServerList_Codec_RoundTripsEntries()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_400_000_000);
        var request = new GatewayBackendServerListRequest(" alpha ", maximumEntries: 16);

        using var requestFrame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_SERVER_LIST,
            GatewayBackendServerListRequest.ProtocolVersion,
            request,
            GatewayBackendServerListRequest.Codec);

        var decodedRequest = PacketCodec.Decode(requestFrame, GatewayBackendServerListRequest.Codec);
        Assert.Equal("alpha", decodedRequest.BackendKind);
        Assert.Equal(16, decodedRequest.MaximumEntries);

        var entry = new GatewayBackendServerListEntry(
            new GatewayBackendServerHandle("server-alpha"),
            "alpha",
            GatewayBackendServerState.Open,
            "v1",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "{\"name\":\"Alpha\"}");
        var response = GatewayBackendServerListResponse.Accepted(
            " alpha ",
            [entry],
            observedAt);

        using var responseFrame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_BACKEND_SERVER_LIST,
            GatewayBackendServerListRequest.ProtocolVersion,
            response,
            GatewayBackendServerListResponse.Codec);

        var decodedResponse = PacketCodec.Decode(responseFrame, GatewayBackendServerListResponse.Codec);

        Assert.True(decodedResponse.Success);
        Assert.Equal("alpha", decodedResponse.BackendKind);
        Assert.Equal(observedAt, decodedResponse.ObservedAt);
        var decodedEntry = Assert.Single(decodedResponse.Entries);
        Assert.Equal(entry.ServerHandle, decodedEntry.ServerHandle);
        Assert.Equal(GatewayBackendServerState.Open, decodedEntry.State);
        Assert.Equal("{\"name\":\"Alpha\"}", decodedEntry.DescriptorJson);
    }

    [Fact]
    public void DataEnvelope_Codec_RoundTrips_RouteAndExchangeMetadata()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
        byte[] payload = [1, 2, 3, 4];
        var envelope = new GatewayBackendRouteDataEnvelope(
            routeToken,
            GatewayBackendRouteDirection.BackendToClient,
            PacketKind.Request,
            routedPacketId: 456,
            routedVersion: 3,
            exchangeId,
            payload);

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_DATA,
            GatewayBackendRouteDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteDataEnvelope.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteDataEnvelope.Codec);

        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal(GatewayBackendRouteDirection.BackendToClient, decoded.Direction);
        Assert.Equal(PacketKind.Request, decoded.RoutedKind);
        Assert.Equal((ushort)456, decoded.RoutedPacketId);
        Assert.Equal((ushort)3, decoded.RoutedVersion);
        Assert.True(decoded.ExchangeId.HasValue);
        Assert.Equal(exchangeId, decoded.ExchangeId.Value);
        Assert.Equal(payload, decoded.RoutedPayload);
    }

    [Fact]
    public void DataEnvelope_Codec_RoundTrips_NotifyWithoutExchangeId()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        byte[] payload = [9, 8, 7];
        var envelope = new GatewayBackendRouteDataEnvelope(
            routeToken,
            GatewayBackendRouteDirection.ClientToBackend,
            PacketKind.Notify,
            routedPacketId: 789,
            routedVersion: 1,
            exchangeId: null,
            payload);

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE_DATA,
            GatewayBackendRouteDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteDataEnvelope.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteDataEnvelope.Codec);

        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal(GatewayBackendRouteDirection.ClientToBackend, decoded.Direction);
        Assert.Equal(PacketKind.Notify, decoded.RoutedKind);
        Assert.False(decoded.ExchangeId.HasValue);
        Assert.Equal(payload, decoded.RoutedPayload);
    }

    [Fact]
    public void ChannelDataEnvelope_Codec_RoundTrips_ChannelAndExchangeMetadata()
    {
        var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
        byte[] payload = [1, 2, 3, 4];
        var envelope = new GatewayBackendChannelDataEnvelope(
            channelId: 37,
            PacketKind.Request,
            routedPacketId: 456,
            routedVersion: 3,
            exchangeId,
            payload);

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_CHANNEL_DATA,
            GatewayBackendChannelDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendChannelDataEnvelope.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec);

        Assert.Equal((uint)37, decoded.ChannelId);
        Assert.Equal(PacketKind.Request, decoded.RoutedKind);
        Assert.Equal((ushort)456, decoded.RoutedPacketId);
        Assert.Equal((ushort)3, decoded.RoutedVersion);
        Assert.True(decoded.ExchangeId.HasValue);
        Assert.Equal(exchangeId, decoded.ExchangeId.Value);
        Assert.Equal(payload, decoded.RoutedPayload);
    }

    [Fact]
    public void ChannelDataEnvelope_Codec_RoundTrips_NotifyWithoutExchangeId()
    {
        byte[] payload = [9, 8, 7];
        var envelope = new GatewayBackendChannelDataEnvelope(
            channelId: 42,
            PacketKind.Notify,
            routedPacketId: 789,
            routedVersion: 1,
            exchangeId: null,
            payload);

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_DATA,
            GatewayBackendChannelDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendChannelDataEnvelope.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec);

        Assert.Equal((uint)42, decoded.ChannelId);
        Assert.Equal(PacketKind.Notify, decoded.RoutedKind);
        Assert.False(decoded.ExchangeId.HasValue);
        Assert.Equal(payload, decoded.RoutedPayload);
    }

    [Fact]
    public void ChannelOpen_Codec_RoundTrips_ChannelAndPrincipal()
    {
        var open = new GatewayBackendChannelOpen(37, "player-1");

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_OPEN,
            GatewayBackendChannelOpen.ProtocolVersion,
            open,
            GatewayBackendChannelOpen.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendChannelOpen.Codec);

        Assert.Equal((uint)37, decoded.ChannelId);
        Assert.Equal("player-1", decoded.PrincipalSubjectId);
    }

    [Fact]
    public void ChannelOpen_Codec_RoundTrips_AnonymousChannel()
    {
        var open = new GatewayBackendChannelOpen(37, principalSubjectId: null);

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_OPEN,
            GatewayBackendChannelOpen.ProtocolVersion,
            open,
            GatewayBackendChannelOpen.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendChannelOpen.Codec);

        Assert.Equal((uint)37, decoded.ChannelId);
        Assert.Null(decoded.PrincipalSubjectId);
    }

    [Fact]
    public void Close_Codec_RoundTrips_RouteToken()
    {
        var routeToken = new GatewayBackendRouteToken("route-token-alpha");
        var close = new GatewayBackendRouteClose(routeToken, "client disconnected");

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_ROUTE_CLOSE,
            GatewayBackendRouteClose.ProtocolVersion,
            close,
            GatewayBackendRouteClose.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRouteClose.Codec);

        Assert.Equal(routeToken, decoded.RouteToken);
        Assert.Equal("client disconnected", decoded.Reason);
    }

    [Fact]
    public void ChannelClose_Codec_RoundTrips_ChannelId()
    {
        var close = new GatewayBackendChannelClose(37, "backend closed");

        using var frame = PacketCodec.Encode(
            PacketKind.Notify,
            Pid.GATE_BACKEND_CHANNEL_CLOSE,
            GatewayBackendChannelClose.ProtocolVersion,
            close,
            GatewayBackendChannelClose.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendChannelClose.Codec);

        Assert.Equal((uint)37, decoded.ChannelId);
        Assert.Equal("backend closed", decoded.Reason);
    }

    [Fact]
    public void RouteToken_Rejects_EmptyOrWhitespaceValues()
    {
        Assert.Throws<ArgumentException>(() => new GatewayBackendRouteToken(string.Empty));
        Assert.Throws<ArgumentException>(() => new GatewayBackendRouteToken("token with space"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayBackendRouteToken(
            new string('a', GatewayBackendRouteToken.MaxLength + 1)));
    }

    [Fact]
    public void ExchangeId_Rejects_EmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new GatewayBackendExchangeId(Guid.Empty));
    }

    [Fact]
    public void DataEnvelope_Rejects_RequestWithoutExchangeId()
    {
        Assert.Throws<ArgumentException>(() => new GatewayBackendRouteDataEnvelope(
            new GatewayBackendRouteToken("route-token-alpha"),
            GatewayBackendRouteDirection.ClientToBackend,
            PacketKind.Request,
            routedPacketId: 123,
            routedVersion: 1,
            exchangeId: null,
            []));
    }

    [Fact]
    public void DataEnvelope_Rejects_ControlPackets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayBackendRouteDataEnvelope(
            new GatewayBackendRouteToken("route-token-alpha"),
            GatewayBackendRouteDirection.ClientToBackend,
            PacketKind.Control,
            routedPacketId: 123,
            routedVersion: 1,
            new GatewayBackendExchangeId(Guid.NewGuid()),
            []));
    }

    [Fact]
    public void DataEnvelope_Rejects_InvalidDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayBackendRouteDataEnvelope(
            new GatewayBackendRouteToken("route-token-alpha"),
            (GatewayBackendRouteDirection)255,
            PacketKind.Notify,
            routedPacketId: 123,
            routedVersion: 1,
            exchangeId: null,
            []));
    }

    [Fact]
    public void ChannelDataEnvelope_Rejects_ZeroChannelId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayBackendChannelDataEnvelope(
            channelId: 0,
            PacketKind.Notify,
            routedPacketId: 123,
            routedVersion: 1,
            exchangeId: null,
            []));
    }

    [Fact]
    public void OpenResponse_Rejects_SuccessWithoutRouteToken()
    {
        Assert.Throws<ArgumentException>(() => new GatewayBackendRouteOpenResponse(
            routeToken: null,
            "alpha",
            success: true,
            errorMessage: string.Empty));
    }
}
