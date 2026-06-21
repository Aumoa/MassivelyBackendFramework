using GatewayServer.Protocols;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class GatewayClientAuthenticationProtocolTests
{
    [Fact]
    public void AuthenticateRequest_Codec_RoundTrips_AccessToken()
    {
        var request = new GatewayClientAuthenticateRequest(" access-token-alpha ");

        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_CLIENT_AUTHENTICATE,
            GatewayClientAuthenticateRequest.ProtocolVersion,
            request,
            GatewayClientAuthenticateRequest.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientAuthenticateRequest.Codec);

        Assert.Equal("access-token-alpha", decoded.AccessToken);
    }

    [Fact]
    public void AuthenticateResponse_Codec_RoundTrips_AcceptedSubject()
    {
        var response = GatewayClientAuthenticateResponse.Accepted(" player-1 ");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_CLIENT_AUTHENTICATE,
            GatewayClientAuthenticateResponse.ProtocolVersion,
            response,
            GatewayClientAuthenticateResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientAuthenticateResponse.Codec);

        Assert.True(decoded.Success);
        Assert.Equal("player-1", decoded.SubjectId);
        Assert.Equal(string.Empty, decoded.ErrorMessage);
    }

    [Fact]
    public void AuthenticateResponse_Codec_RoundTrips_Rejection()
    {
        var response = GatewayClientAuthenticateResponse.Rejected("invalid token");

        using var frame = PacketCodec.Encode(
            PacketKind.Response,
            Pid.GATE_CLIENT_AUTHENTICATE,
            GatewayClientAuthenticateResponse.ProtocolVersion,
            response,
            GatewayClientAuthenticateResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientAuthenticateResponse.Codec);

        Assert.False(decoded.Success);
        Assert.Equal(string.Empty, decoded.SubjectId);
        Assert.Equal("invalid token", decoded.ErrorMessage);
    }

    [Fact]
    public void AuthenticateRequest_Rejects_EmptyAccessToken()
    {
        Assert.Throws<ArgumentException>(() => new GatewayClientAuthenticateRequest(string.Empty));
        Assert.Throws<ArgumentException>(() => new GatewayClientAuthenticateRequest(" "));
    }

    [Fact]
    public void AuthenticateResponse_Rejects_SuccessWithoutSubject()
    {
        Assert.Throws<ArgumentException>(() => GatewayClientAuthenticateResponse.Accepted(string.Empty));
    }
}
