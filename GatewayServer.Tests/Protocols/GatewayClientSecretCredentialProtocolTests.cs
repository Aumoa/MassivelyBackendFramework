using MasterServer.ControlPlane;
using MasterServer.Services;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class GatewayClientSecretCredentialProtocolTests
{
    [Fact]
    public void Snapshot_Codec_RoundTripsSecrets()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var snapshot = new GatewayClientSecretCredentialSnapshot(
            [new GatewayClientSecretValidationInfo("token-a", "player-1", "hash-a")],
            observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayClientSecretCredentialSnapshot,
            version: 1,
            snapshot,
            GatewayClientSecretCredentialSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientSecretCredentialSnapshot.Codec);

        Assert.Equal(observedAt, decoded.ObservedAt);
        var secret = Assert.Single(decoded.Secrets);
        Assert.Equal("token-a", secret.TokenId);
        Assert.Equal("player-1", secret.SubjectId);
        Assert.Equal("hash-a", secret.SecretHash);
    }

    [Fact]
    public void ManagementRequest_Codec_RoundTripsCreate()
    {
        var requestId = Guid.NewGuid();
        var request = GatewayClientSecretCredentialManagementRequest.Create(
            requestId,
            new GatewayClientSecretCredentialInput(
                "player-1",
                "Local client",
                enabled: true));

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayClientSecretCredentialManagementRequest,
            version: 1,
            request,
            GatewayClientSecretCredentialManagementRequest.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientSecretCredentialManagementRequest.Codec);

        Assert.Equal(requestId, decoded.RequestId);
        Assert.Equal(GatewayClientSecretCredentialOperation.Create, decoded.Operation);
        Assert.Equal("player-1", decoded.SubjectId);
        Assert.Equal("Local client", decoded.DisplayName);
        Assert.True(decoded.Enabled);
    }

    [Fact]
    public void ManagementResponse_Codec_RoundTripsAccessToken()
    {
        var requestId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 22, 1, 2, 3, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(1);
        var response = GatewayClientSecretCredentialManagementResponse.SuccessResult(
            requestId,
            [
                new GatewayClientSecretCredentialInfo(
                    id: 7,
                    tokenId: "token-a",
                    subjectId: "player-1",
                    displayName: "Local client",
                    enabled: true,
                    createdAt,
                    updatedAt)
            ],
            "gwc_token-a.secret-alpha");

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayClientSecretCredentialManagementResponse,
            version: 1,
            response,
            GatewayClientSecretCredentialManagementResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayClientSecretCredentialManagementResponse.Codec);

        Assert.Equal(requestId, decoded.RequestId);
        Assert.True(decoded.Success);
        Assert.Equal("gwc_token-a.secret-alpha", decoded.AccessToken);
        var credential = Assert.Single(decoded.Credentials);
        Assert.Equal(7, credential.Id);
        Assert.Equal("token-a", credential.TokenId);
        Assert.Equal("player-1", credential.SubjectId);
        Assert.Equal("Local client", credential.DisplayName);
        Assert.True(credential.Enabled);
        Assert.Equal(createdAt, credential.CreatedAt);
        Assert.Equal(updatedAt, credential.UpdatedAt);
    }
}
