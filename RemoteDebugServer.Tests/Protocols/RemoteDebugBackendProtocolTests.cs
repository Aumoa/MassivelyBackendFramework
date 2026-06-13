using PacketCore;
using RemoteDebugServer.Protocols;
using Xunit;

namespace RemoteDebugServer.Tests.Protocols;

public sealed class RemoteDebugBackendProtocolTests
{
    [Fact]
    public void StatusRequest_CodecRoundTrips_EmptyPayload()
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            RemoteDebugPacketIds.BackendStatusRequest,
            RemoteDebugProtocol.SchemaVersion,
            RemoteDebugBackendStatusRequest.Instance,
            RemoteDebugBackendStatusRequest.Codec);

        var decoded = PacketCodec.Decode(frame, RemoteDebugBackendStatusRequest.Codec);

        Assert.Same(RemoteDebugBackendStatusRequest.Instance, decoded);
        Assert.Equal(0, frame.Header.PayloadLength);
    }

    [Fact]
    public void StatusResponse_CodecRoundTrips_BackendCounts()
    {
        var response = new RemoteDebugBackendStatusResponse(
            " UnityRemoteDebug ",
            gatewayConnectionCount: 2,
            remoteDebugClientCount: 3,
            observedAtUnixTimeMilliseconds: 123456789);

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            RemoteDebugPacketIds.BackendStatusResponse,
            RemoteDebugProtocol.SchemaVersion,
            response,
            RemoteDebugBackendStatusResponse.Codec);

        var decoded = PacketCodec.Decode(frame, RemoteDebugBackendStatusResponse.Codec);

        Assert.Equal("UnityRemoteDebug", decoded.BackendKind);
        Assert.Equal(2, decoded.GatewayConnectionCount);
        Assert.Equal(3, decoded.RemoteDebugClientCount);
        Assert.Equal(123456789, decoded.ObservedAtUnixTimeMilliseconds);
    }

    [Fact]
    public void ClientListResponse_CodecRoundTrips_ClientSnapshots()
    {
        var response = new RemoteDebugBackendClientListResponse(
            [
                new RemoteDebugBackendClientSnapshot(
                    "client-a",
                    "Editor",
                    "1.2.3",
                    "6000.0.1f1",
                    RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.RemoteControl,
                    connectedAtUnixTimeMilliseconds: 1000)
            ],
            observedAtUnixTimeMilliseconds: 2000);

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            RemoteDebugPacketIds.BackendClientListResponse,
            RemoteDebugProtocol.SchemaVersion,
            response,
            RemoteDebugBackendClientListResponse.Codec);

        var decoded = PacketCodec.Decode(frame, RemoteDebugBackendClientListResponse.Codec);

        Assert.Equal(2000, decoded.ObservedAtUnixTimeMilliseconds);
        var client = Assert.Single(decoded.Clients);
        Assert.Equal("client-a", client.ClientId);
        Assert.Equal("Editor", client.DisplayName);
        Assert.Equal("1.2.3", client.ClientVersion);
        Assert.Equal("6000.0.1f1", client.UnityVersion);
        Assert.Equal(RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.RemoteControl, client.Capabilities);
        Assert.Equal(1000, client.ConnectedAtUnixTimeMilliseconds);
    }

    [Fact]
    public void ErrorResponse_CodecRoundTrips_RequestMetadata()
    {
        var response = new RemoteDebugBackendErrorResponse(
            RemoteDebugPacketIds.BackendStatusRequest,
            RemoteDebugProtocol.SchemaVersion,
            "Unsupported request.");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            RemoteDebugPacketIds.BackendErrorResponse,
            RemoteDebugProtocol.SchemaVersion,
            response,
            RemoteDebugBackendErrorResponse.Codec);

        var decoded = PacketCodec.Decode(frame, RemoteDebugBackendErrorResponse.Codec);

        Assert.Equal(RemoteDebugPacketIds.BackendStatusRequest, decoded.RequestPacketId);
        Assert.Equal(RemoteDebugProtocol.SchemaVersion, decoded.RequestVersion);
        Assert.Equal("Unsupported request.", decoded.ErrorMessage);
    }
}
