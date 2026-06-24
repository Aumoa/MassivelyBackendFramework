using MasterServer.ControlPlane;
using PacketCore;
using Xunit;

namespace MasterServer.Abstractions.Tests.ControlPlane;

public sealed class MasterOverviewSnapshotProtocolTests
{
    [Fact]
    public void Codec_RoundTripsConnectionIdentityFields()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_001_234);
        var connectedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_000_000);
        var lastSeenAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_000_500);
        var connectionId = Guid.NewGuid();
        var snapshot = new MasterOverviewSnapshot(
            new MasterSocketEndpoint("127.0.0.1", 17000, useTls: true),
            [
                new MasterConnectionSnapshot(
                    connectionId,
                    "10.0.0.5:44321",
                    MasterNodeKind.Backend,
                    "inventory-backend-1",
                    "Inventory Backend",
                    "inventory",
                    connectedAt,
                    lastSeenAt)
            ],
            observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.OverviewSnapshot,
            MasterControlProtocol.SchemaVersion,
            snapshot,
            MasterOverviewSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, MasterOverviewSnapshot.Codec);

        Assert.Equal("127.0.0.1", decoded.SocketEndpoint.IPAddress);
        Assert.Equal(17000, decoded.SocketEndpoint.Port);
        Assert.True(decoded.SocketEndpoint.UseTls);
        Assert.Equal(observedAt, decoded.ObservedAt);

        var connection = Assert.Single(decoded.Connections);
        Assert.Equal(connectionId, connection.ConnectionId);
        Assert.Equal("10.0.0.5:44321", connection.RemoteEndPoint);
        Assert.Equal(MasterNodeKind.Backend, connection.NodeKind);
        Assert.Equal("inventory-backend-1", connection.NodeId);
        Assert.Equal("Inventory Backend", connection.DisplayName);
        Assert.Equal("inventory", connection.BackendKind);
        Assert.Equal(connectedAt, connection.ConnectedAt);
        Assert.Equal(lastSeenAt, connection.LastSeenAt);
    }

    [Fact]
    public void Codec_RoundTripsEmptyIdentityFieldsFromLegacyConstructor()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_010_000);
        var connectedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_000_000);
        var lastSeenAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_000_000_500);
        var connectionId = Guid.NewGuid();
        var snapshot = new MasterOverviewSnapshot(
            new MasterSocketEndpoint("unknown", 0, useTls: false),
            [
                new MasterConnectionSnapshot(
                    connectionId,
                    "unknown",
                    MasterNodeKind.Unknown,
                    connectedAt,
                    lastSeenAt)
            ],
            observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.OverviewSnapshot,
            MasterControlProtocol.SchemaVersion,
            snapshot,
            MasterOverviewSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, MasterOverviewSnapshot.Codec);

        Assert.Equal("unknown", decoded.SocketEndpoint.IPAddress);
        Assert.Equal(0, decoded.SocketEndpoint.Port);
        Assert.False(decoded.SocketEndpoint.UseTls);
        Assert.Equal(observedAt, decoded.ObservedAt);

        var connection = Assert.Single(decoded.Connections);
        Assert.Equal(connectionId, connection.ConnectionId);
        Assert.Equal("unknown", connection.RemoteEndPoint);
        Assert.Equal(MasterNodeKind.Unknown, connection.NodeKind);
        Assert.Equal(string.Empty, connection.NodeId);
        Assert.Equal(string.Empty, connection.DisplayName);
        Assert.Equal(string.Empty, connection.BackendKind);
        Assert.Equal(connectedAt, connection.ConnectedAt);
        Assert.Equal(lastSeenAt, connection.LastSeenAt);
    }
}
