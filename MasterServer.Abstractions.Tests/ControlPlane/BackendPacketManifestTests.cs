using MasterServer.ControlPlane;
using PacketCore;
using Xunit;

namespace MasterServer.Abstractions.Tests.ControlPlane;

public sealed class BackendPacketManifestTests
{
    [Fact]
    public void ManifestId_NormalizesAndRejectsUnsafeValues()
    {
        var manifestId = new BackendPacketManifestId(" inventory:v1 ");

        Assert.Equal("inventory:v1", manifestId.Value);
        Assert.Throws<ArgumentException>(() => new BackendPacketManifestId("inventory/v1"));
        Assert.Throws<ArgumentException>(() => new BackendPacketManifestId(" "));
    }

    [Fact]
    public void ManifestHash_NormalizesHex()
    {
        var hash = new BackendPacketManifestHash("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789");

        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", hash.Value);
        Assert.Throws<ArgumentException>(() => new BackendPacketManifestHash("abc"));
        Assert.Throws<ArgumentException>(() => new BackendPacketManifestHash("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"));
    }

    [Fact]
    public void Constructor_ComputesStableHashIndependentOfInputOrder()
    {
        var first = new BackendPacketManifest(
            " inventory ",
            new BackendPacketManifestId("v1"),
            [
                CreateEntry(BackendPacketManifestDirection.BackendToClient, PacketKind.Notify, 201, 1, 1, 8),
                CreateEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Request, 101, 2, 4, 16)
            ]);
        var second = new BackendPacketManifest(
            "inventory",
            new BackendPacketManifestId("v1"),
            [
                CreateEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Request, 101, 2, 4, 16),
                CreateEntry(BackendPacketManifestDirection.BackendToClient, PacketKind.Notify, 201, 1, 1, 8)
            ]);

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal("inventory", first.BackendKind);
        Assert.Equal(BackendPacketManifestDirection.ClientToBackend, first.Entries[0].Direction);
        Assert.Equal(PacketKind.Request, first.Entries[0].PacketKind);
    }

    [Fact]
    public void Constructor_RejectsDuplicateEntries()
    {
        Assert.Throws<ArgumentException>(() => new BackendPacketManifest(
            "inventory",
            new BackendPacketManifestId("v1"),
            [
                CreateEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Request, 101, 1, 0, 16),
                CreateEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Request, 101, 1, 0, 16)
            ]));
    }

    [Fact]
    public void ValidatePacket_AcceptsMatchingPayload()
    {
        var manifest = CreateManifest();

        var result = manifest.ValidatePacket(
            BackendPacketManifestDirection.ClientToBackend,
            PacketKind.Request,
            101,
            2,
            payloadLength: 8);

        Assert.True(result.Success);
        Assert.Equal(BackendPacketManifestValidationFailure.None, result.Failure);
        Assert.NotNull(result.Entry);
    }

    [Theory]
    [InlineData(1, BackendPacketManifestValidationFailure.PayloadTooSmall)]
    [InlineData(17, BackendPacketManifestValidationFailure.PayloadTooLarge)]
    public void ValidatePacket_RejectsPayloadOutsideRange(
        int payloadLength,
        BackendPacketManifestValidationFailure expectedFailure)
    {
        var manifest = CreateManifest();

        var result = manifest.ValidatePacket(
            BackendPacketManifestDirection.ClientToBackend,
            PacketKind.Request,
            101,
            2,
            payloadLength);

        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.Failure);
    }

    [Fact]
    public void ValidatePacket_RejectsPayloadThatDoesNotMatchFixedLength()
    {
        var manifest = new BackendPacketManifest(
            "inventory",
            new BackendPacketManifestId("v1"),
            [
                new BackendPacketManifestEntry(
                    BackendPacketManifestDirection.ClientToBackend,
                    PacketKind.Notify,
                    301,
                    1,
                    new BackendPacketPayloadConstraint(0, 8, fixedLength: 4))
            ]);

        var result = manifest.ValidatePacket(
            BackendPacketManifestDirection.ClientToBackend,
            PacketKind.Notify,
            301,
            1,
            payloadLength: 3);

        Assert.False(result.Success);
        Assert.Equal(BackendPacketManifestValidationFailure.PayloadLengthMismatch, result.Failure);
    }

    [Theory]
    [InlineData(PacketKind.Response, 101, 2, BackendPacketManifestValidationFailure.PacketKindMismatch)]
    [InlineData(PacketKind.Request, 101, 9, BackendPacketManifestValidationFailure.UnknownVersion)]
    [InlineData(PacketKind.Request, 999, 1, BackendPacketManifestValidationFailure.UnknownPacketId)]
    public void ValidatePacket_ReportsCompatibilityFailure(
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        BackendPacketManifestValidationFailure expectedFailure)
    {
        var manifest = CreateManifest();

        var result = manifest.ValidatePacket(
            BackendPacketManifestDirection.ClientToBackend,
            packetKind,
            packetId,
            routedVersion,
            payloadLength: 8);

        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.Failure);
    }

    private static BackendPacketManifest CreateManifest()
    {
        return new BackendPacketManifest(
            "inventory",
            new BackendPacketManifestId("v1"),
            [
                CreateEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Request, 101, 2, 4, 16)
            ]);
    }

    private static BackendPacketManifestEntry CreateEntry(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        int minimumLength,
        int maximumLength)
    {
        return new BackendPacketManifestEntry(
            direction,
            packetKind,
            packetId,
            routedVersion,
            new BackendPacketPayloadConstraint(minimumLength, maximumLength));
    }
}
