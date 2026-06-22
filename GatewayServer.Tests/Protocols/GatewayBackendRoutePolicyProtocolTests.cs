using MasterServer.ControlPlane;
using MasterServer.Services;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class GatewayBackendRoutePolicyProtocolTests
{
    [Fact]
    public void Snapshot_Codec_RoundTripsAllowedBackendKinds()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var snapshot = new GatewayBackendRoutePolicySnapshot(["alpha", "beta"], observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayBackendRoutePolicySnapshot,
            version: 1,
            snapshot,
            GatewayBackendRoutePolicySnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRoutePolicySnapshot.Codec);

        Assert.Equal(["alpha", "beta"], decoded.AllowedBackendKinds);
        Assert.Equal(observedAt, decoded.ObservedAt);
    }

    [Fact]
    public void ManagementRequest_Codec_RoundTripsUpdate()
    {
        var requestId = Guid.NewGuid();
        var request = GatewayBackendRoutePolicyManagementRequest.Update(
            requestId,
            entryId: 42,
            new GatewayBackendRoutePolicyEntryInput(" alpha ", enabled: false));

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayBackendRoutePolicyManagementRequest,
            version: 1,
            request,
            GatewayBackendRoutePolicyManagementRequest.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRoutePolicyManagementRequest.Codec);

        Assert.Equal(requestId, decoded.RequestId);
        Assert.Equal(GatewayBackendRoutePolicyOperation.Update, decoded.Operation);
        Assert.Equal(42, decoded.EntryId);
        Assert.Equal("alpha", decoded.BackendKind);
        Assert.False(decoded.Enabled);
    }

    [Fact]
    public void ManagementResponse_Codec_RoundTripsEntries()
    {
        var requestId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 22, 1, 2, 3, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(1);
        var response = GatewayBackendRoutePolicyManagementResponse.SuccessResult(
            requestId,
            [
                new GatewayBackendRoutePolicyEntryInfo(
                    id: 7,
                    backendKind: "alpha",
                    enabled: true,
                    createdAt,
                    updatedAt)
            ]);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.GatewayBackendRoutePolicyManagementResponse,
            version: 1,
            response,
            GatewayBackendRoutePolicyManagementResponse.Codec);

        var decoded = PacketCodec.Decode(frame, GatewayBackendRoutePolicyManagementResponse.Codec);

        Assert.Equal(requestId, decoded.RequestId);
        Assert.True(decoded.Success);
        Assert.Equal(string.Empty, decoded.ErrorMessage);
        var entry = Assert.Single(decoded.Entries);
        Assert.Equal(7, entry.Id);
        Assert.Equal("alpha", entry.BackendKind);
        Assert.True(entry.Enabled);
        Assert.Equal(createdAt, entry.CreatedAt);
        Assert.Equal(updatedAt, entry.UpdatedAt);
    }
}
