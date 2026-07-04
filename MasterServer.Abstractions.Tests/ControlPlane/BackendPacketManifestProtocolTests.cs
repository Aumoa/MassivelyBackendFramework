using MasterServer.ControlPlane;
using MasterServer.Services;
using PacketCore;
using Xunit;

namespace MasterServer.Abstractions.Tests.ControlPlane;

public sealed class BackendPacketManifestProtocolTests
{
    [Fact]
    public void SnapshotCodec_RoundTripsManifest()
    {
        var manifest = CreateManifest();
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_200_000_000);
        var snapshot = new BackendPacketManifestSnapshot([manifest], observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendPacketManifestSnapshot,
            MasterControlProtocol.SchemaVersion,
            snapshot,
            BackendPacketManifestSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, BackendPacketManifestSnapshot.Codec);

        Assert.Equal(observedAt, decoded.ObservedAt);
        var decodedManifest = Assert.Single(decoded.Manifests);
        Assert.Equal("inventory", decodedManifest.BackendKind);
        Assert.Equal(manifest.ManifestId, decodedManifest.ManifestId);
        Assert.Equal(manifest.Hash, decodedManifest.Hash);
        var entry = Assert.Single(decodedManifest.Entries);
        Assert.Equal(BackendPacketManifestDirection.ClientToBackend, entry.Direction);
        Assert.Equal(PacketKind.Request, entry.PacketKind);
        Assert.Equal(101, entry.PacketId);
        Assert.Equal(2, entry.RoutedVersion);
        Assert.Equal(4, entry.PayloadConstraint.MinimumLength);
        Assert.Equal(16, entry.PayloadConstraint.MaximumLength);
    }

    [Fact]
    public void ManagementRequestCodec_RoundTripsCreateRequest()
    {
        var requestId = Guid.NewGuid();
        var manifest = CreateManifest();
        var request = BackendPacketManifestManagementRequest.Create(
            requestId,
            manifest,
            "initial approval");

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendPacketManifestManagementRequest,
            MasterControlProtocol.SchemaVersion,
            request,
            BackendPacketManifestManagementRequest.Codec);

        var decoded = PacketCodec.Decode(frame, BackendPacketManifestManagementRequest.Codec);

        Assert.Equal(requestId, decoded.RequestId);
        Assert.Equal(BackendPacketManifestOperation.Create, decoded.Operation);
        Assert.Equal("initial approval", decoded.AuditNote);
        Assert.NotNull(decoded.Manifest);
        Assert.Equal(manifest.Hash, decoded.Manifest!.Hash);
    }

    [Fact]
    public void ManagementResponseCodec_RoundTripsManifestInfo()
    {
        var requestId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);
        var deprecatedAt = updatedAt.AddMinutes(10);
        var response = BackendPacketManifestManagementResponse.SuccessResult(
            requestId,
            [
                new BackendPacketManifestInfo(
                    7,
                    CreateManifest(),
                    BackendPacketManifestLifecycle.Deprecated,
                    "rolling out v2",
                    createdAt,
                    updatedAt,
                    deprecatedAt)
            ]);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendPacketManifestManagementResponse,
            MasterControlProtocol.SchemaVersion,
            response,
            BackendPacketManifestManagementResponse.Codec);

        var decoded = PacketCodec.Decode(frame, BackendPacketManifestManagementResponse.Codec);

        Assert.True(decoded.Success);
        var manifestInfo = Assert.Single(decoded.Manifests);
        Assert.Equal(7, manifestInfo.Id);
        Assert.Equal(BackendPacketManifestLifecycle.Deprecated, manifestInfo.Lifecycle);
        Assert.Equal("rolling out v2", manifestInfo.AuditNote);
        Assert.Equal(createdAt, manifestInfo.CreatedAt);
        Assert.Equal(updatedAt, manifestInfo.UpdatedAt);
        Assert.Equal(deprecatedAt, manifestInfo.DeprecatedAt);
        Assert.Equal("inventory", manifestInfo.Manifest.BackendKind);
    }

    [Fact]
    public void BackendEndpointAdvertiseCodec_RoundTripsManifestIdentity()
    {
        var manifest = CreateManifest();
        var advertise = new BackendEndpointAdvertise(
            "inventory",
            new MasterSocketEndpoint("127.0.0.1", 19001, useTls: true),
            manifest.ManifestId,
            manifest.Hash);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendEndpointAdvertise,
            MasterControlProtocol.SchemaVersion,
            advertise,
            BackendEndpointAdvertise.Codec);

        var decoded = PacketCodec.Decode(frame, BackendEndpointAdvertise.Codec);

        Assert.Equal("inventory", decoded.BackendKind);
        Assert.Equal("127.0.0.1", decoded.GatewayEndpoint.IPAddress);
        Assert.Equal(19001, decoded.GatewayEndpoint.Port);
        Assert.True(decoded.GatewayEndpoint.UseTls);
        Assert.Equal(manifest.ManifestId, decoded.ManifestId);
        Assert.Equal(manifest.Hash, decoded.ManifestHash);
    }

    [Fact]
    public void SnapshotCodec_RoundTripsVerifierProgram()
    {
        var verifierProgram = new BackendPacketVerifierProgram(
            [
                BackendPacketVerifierInstruction.ReadUInt8(targetSlot: 0),
                BackendPacketVerifierInstruction.ReadUtf8String(
                    BackendPacketVerifierLengthConstraint.Dynamic(
                        sourceSlot: 0,
                        minimumLength: 0,
                        maximumLength: 16))
            ]);
        var manifest = new BackendPacketManifest(
            "chat",
            new BackendPacketManifestId("v1"),
            [
                new BackendPacketManifestEntry(
                    BackendPacketManifestDirection.ClientToBackend,
                    PacketKind.Notify,
                    501,
                    1,
                    new BackendPacketPayloadConstraint(
                        1,
                        17,
                        verifierProgram: verifierProgram))
            ]);
        var snapshot = new BackendPacketManifestSnapshot([manifest], DateTimeOffset.UtcNow);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendPacketManifestSnapshot,
            MasterControlProtocol.SchemaVersion,
            snapshot,
            BackendPacketManifestSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, BackendPacketManifestSnapshot.Codec);
        var decodedEntry = Assert.Single(Assert.Single(decoded.Manifests).Entries);

        Assert.NotNull(decodedEntry.PayloadConstraint.VerifierProgram);
        Assert.True(decodedEntry.PayloadConstraint.VerifierProgram!.Verify(new byte[] { 2, 65, 66 }).Success);
        Assert.Equal(manifest.Hash, decoded.Manifests[0].Hash);
    }

    private static BackendPacketManifest CreateManifest()
    {
        return new BackendPacketManifest(
            "inventory",
            new BackendPacketManifestId("v1"),
            [
                new BackendPacketManifestEntry(
                    BackendPacketManifestDirection.ClientToBackend,
                    PacketKind.Request,
                    101,
                    2,
                    new BackendPacketPayloadConstraint(4, 16))
            ]);
    }
}
