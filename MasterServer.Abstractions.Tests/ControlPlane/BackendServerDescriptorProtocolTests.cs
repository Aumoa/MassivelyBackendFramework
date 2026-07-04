using System.Text;
using MasterServer.ControlPlane;
using PacketCore;
using Xunit;

namespace MasterServer.Abstractions.Tests.ControlPlane;

public sealed class BackendServerDescriptorProtocolTests
{
    [Fact]
    public void BackendEndpointAdvertiseCodec_RoundTripsDescriptor()
    {
        const string descriptorJson = "{\"name\":\"Alpha\",\"region\":\"KR\"}";
        var advertise = new BackendEndpointAdvertise(
            "inventory",
            new MasterSocketEndpoint("127.0.0.1", 19001, useTls: true),
            new BackendPacketManifestId("v1"),
            new BackendPacketManifestHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            BackendNodeState.Draining,
            "2026.07",
            descriptorJson);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendEndpointAdvertise,
            MasterControlProtocol.SchemaVersion,
            advertise,
            BackendEndpointAdvertise.Codec);

        var decoded = PacketCodec.Decode(frame, BackendEndpointAdvertise.Codec);

        Assert.Equal(BackendNodeState.Draining, decoded.Descriptor.State);
        Assert.Equal("2026.07", decoded.Descriptor.DescriptorVersion);
        Assert.Equal(descriptorJson, decoded.Descriptor.DescriptorJson);
        Assert.Equal(
            BackendServerDescriptor.ComputeDescriptorHash(descriptorJson),
            decoded.Descriptor.DescriptorHash);
    }

    [Fact]
    public void BackendNodeSnapshotCodec_RoundTripsDescriptor()
    {
        const string descriptorJson = "{\"name\":\"Alpha\",\"ruleset\":\"pve\"}";
        var descriptor = BackendServerDescriptor.Create(
            BackendNodeState.Full,
            "v2",
            descriptorJson);
        var advertisedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_774_300_000_000);
        var observedAt = advertisedAt.AddSeconds(1);
        var snapshot = new BackendNodeSnapshot(
            [
                new BackendNodeEndpoint(
                    "world",
                    "world-1",
                    "World 1",
                    "master-connection-1",
                    new MasterSocketEndpoint("10.0.0.12", 19002, useTls: true),
                    new BackendPacketManifestId("world-v2"),
                    new BackendPacketManifestHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
                    descriptor,
                    advertisedAt)
            ],
            observedAt);

        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            MasterControlPacketIds.BackendNodeSnapshot,
            MasterControlProtocol.SchemaVersion,
            snapshot,
            BackendNodeSnapshot.Codec);

        var decoded = PacketCodec.Decode(frame, BackendNodeSnapshot.Codec);

        Assert.Equal(observedAt, decoded.ObservedAt);
        var node = Assert.Single(decoded.Nodes);
        Assert.Equal("world", node.BackendKind);
        Assert.Equal("world-1", node.NodeId);
        Assert.Equal("master-connection-1", node.MasterConnectionId);
        Assert.Equal(BackendNodeState.Full, node.State);
        Assert.Equal("v2", node.DescriptorVersion);
        Assert.Equal(descriptor.DescriptorHash, node.DescriptorHash);
        Assert.Equal(descriptorJson, node.DescriptorJson);
    }

    [Fact]
    public void Descriptor_RejectsPrivateInfrastructurePropertyNames()
    {
        Assert.Throws<ArgumentException>(() => BackendServerDescriptor.Create(
            BackendNodeState.Open,
            "v1",
            "{\"internalEndpoint\":\"10.0.0.12:19002\"}"));
    }

    [Fact]
    public void Descriptor_RejectsJsonBeyondMaximumDepth()
    {
        var json = new StringBuilder();
        json.Append("{\"layers\":");
        for (var i = 0; i < BackendServerDescriptor.MaxDescriptorJsonDepth + 1; i++)
        {
            json.Append('[');
        }

        json.Append('0');
        for (var i = 0; i < BackendServerDescriptor.MaxDescriptorJsonDepth + 1; i++)
        {
            json.Append(']');
        }

        json.Append('}');

        Assert.Throws<ArgumentException>(() => BackendServerDescriptor.Create(
            BackendNodeState.Open,
            "v1",
            json.ToString()));
    }

    [Fact]
    public void Descriptor_AllowsDisplayDescriptionFields()
    {
        var descriptor = BackendServerDescriptor.Create(
            BackendNodeState.Open,
            "v1",
            "{\"description\":\"A relaxed PvE world\"}");

        Assert.Equal("{\"description\":\"A relaxed PvE world\"}", descriptor.DescriptorJson);
    }
}
