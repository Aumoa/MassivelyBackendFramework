using BackendServer.Services;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class BackendCppWireVectorTests
{
    private static readonly Guid s_VectorGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public void PacketCoreHeader_UsesExpectedPackedBigEndianLayout()
    {
        var header = new PacketHeader(
            PacketKind.Control,
            PacketFlags.None,
            MasterControlPacketIds.NodeAuthChallenge,
            MasterControlProtocol.SchemaVersion,
            payloadLength: 0);
        Span<byte> bytes = stackalloc byte[PacketHeader.Size];
        header.Write(bytes);

        Assert.Equal("c000640008000000", ToHex(bytes));
    }

    [Fact]
    public void GatewayBackendChannelOpen_VectorMatchesWireContract()
    {
        var open = new GatewayBackendChannelOpen(
            0x01020304,
            "player-1");

        Assert.Equal(
            "80000a0001000011010203040100000008706c617965722d31",
            EncodeFrameHex(
                PacketKind.Notify,
                Pid.GATE_BACKEND_CHANNEL_OPEN,
                GatewayBackendChannelOpen.ProtocolVersion,
                open,
                GatewayBackendChannelOpen.Codec));
    }

    [Fact]
    public void GatewayBackendChannelDataRequest_VectorMatchesWireContract()
    {
        var envelope = new GatewayBackendChannelDataEnvelope(
            0x01020304,
            PacketKind.Request,
            0x1234,
            2,
            new GatewayBackendExchangeId(s_VectorGuid),
            [0xde, 0xad, 0xbe, 0xef]);

        Assert.Equal(
            "00000800010000220102030400123400020133221100554477668899aabbccddeeff00000004deadbeef",
            EncodeFrameHex(
                PacketKind.Request,
                Pid.GATE_BACKEND_CHANNEL_DATA,
                GatewayBackendChannelDataEnvelope.ProtocolVersion,
                envelope,
                GatewayBackendChannelDataEnvelope.Codec));
    }

    [Fact]
    public void GatewayBackendChannelClose_VectorMatchesWireContract()
    {
        var close = new GatewayBackendChannelClose(
            0x01020304,
            "done");

        Assert.Equal(
            "800009000100000c0102030400000004646f6e65",
            EncodeFrameHex(
                PacketKind.Notify,
                Pid.GATE_BACKEND_CHANNEL_CLOSE,
                GatewayBackendChannelClose.ProtocolVersion,
                close,
                GatewayBackendChannelClose.Codec));
    }

    [Fact]
    public void SidecarDirectConnectValidationRequest_VectorMatchesWireContract()
    {
        var request = new DirectConnectCodeValidationRequest(
            s_VectorGuid,
            "code-1",
            "gateway-a",
            "master-a");

        Assert.Equal(
            "c00001000100003333221100554477668899aabbccddeeff00000006636f64652d3100000009676174657761792d61000000086d61737465722d61",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.DirectConnectCodeValidationRequest,
                BackendSidecarControlProtocol.SchemaVersion,
                request,
                DirectConnectCodeValidationRequest.Codec));
    }

    [Fact]
    public void SidecarManifestSnapshotRequest_VectorMatchesWireContract()
    {
        var request = new SidecarManifestSnapshotRequest(s_VectorGuid);

        Assert.Equal(
            "c0000b000100001033221100554477668899aabbccddeeff",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ManifestSnapshotRequest,
                BackendSidecarControlProtocol.SchemaVersion,
                request,
                SidecarManifestSnapshotRequest.Codec));
    }

    private static string EncodeFrameHex<TPacket>(
        PacketKind kind,
        ushort packetId,
        ushort version,
        TPacket value,
        IPacketCodec<TPacket> codec)
    {
        using var frame = PacketCodec.Encode(kind, packetId, version, value, codec);
        var bytes = new byte[PacketHeader.Size + frame.Payload.Length];
        frame.Header.Write(bytes.AsSpan(0, PacketHeader.Size));
        frame.Payload.Span.CopyTo(bytes.AsSpan(PacketHeader.Size));
        return ToHex(bytes);
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
