using BackendServer.Services;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Protocols;

public sealed class BackendCppWireVectorTests
{
    private static readonly Guid s_VectorGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private const string s_ManifestHash = "af369db2e71fd543ac449aea36b702cacab2799fe1017bec50e84c868453da05";

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

        Assert.Equal("c00064000b000000", ToHex(bytes));
    }

    [Fact]
    public void NodeAuthChallenge_VectorMatchesWireContract()
    {
        Assert.Equal(
            "c00064000b0000330000000b6368616c6c656e67652d6100000020000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
            EncodeFrameHex(
                PacketKind.Control,
                MasterControlPacketIds.NodeAuthChallenge,
                MasterControlProtocol.SchemaVersion,
                CreateChallenge(),
                NodeAuthChallenge.Codec));
    }

    [Fact]
    public void NodeHello_VectorMatchesWireContract()
    {
        var hello = new NodeHello(
            MasterNodeKind.Gateway,
            "gateway-a",
            "Gateway A",
            MasterControlProtocol.SchemaVersion,
            "gateway-master-a");

        Assert.Equal(
            "c00065000b00003101000b00000009676174657761792d610000000947617465776179204100000010676174657761792d6d61737465722d61",
            EncodeFrameHex(
                PacketKind.Control,
                MasterControlPacketIds.NodeHello,
                MasterControlProtocol.SchemaVersion,
                hello,
                NodeHello.Codec));
    }

    [Fact]
    public void DirectConnectCode_VectorMatchesWireContract()
    {
        var code = new DirectConnectCode("code-1");

        Assert.Equal(
            "c00073000b00000a00000006636f64652d31",
            EncodeFrameHex(
                PacketKind.Control,
                MasterControlPacketIds.DirectConnectCode,
                MasterControlProtocol.SchemaVersion,
                code,
                DirectConnectCode.Codec));
    }

    [Fact]
    public void NodeAccepted_VectorMatchesWireContract()
    {
        var accepted = new NodeAccepted("gateway-a", "backend-connection-a");

        Assert.Equal(
            "c00067000b00002500000009676174657761792d61000000146261636b656e642d636f6e6e656374696f6e2d61",
            EncodeFrameHex(
                PacketKind.Control,
                MasterControlPacketIds.NodeAccepted,
                MasterControlProtocol.SchemaVersion,
                accepted,
                NodeAccepted.Codec));
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
    public void SidecarDirectConnectValidationResponse_VectorMatchesWireContract()
    {
        var response = new DirectConnectCodeValidationResponse(
            s_VectorGuid,
            success: true,
            "gateway-a",
            "gateway-master-a",
            MasterNodeKind.Backend,
            "cpp-backend",
            "backend-master-a",
            string.Empty);

        Assert.Equal(
            "c00002000100005a33221100554477668899aabbccddeeff0100000009676174657761792d6100000010676174657761792d6d61737465722d61040000000b6370702d6261636b656e64000000106261636b656e642d6d61737465722d6100000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.DirectConnectCodeValidationResponse,
                BackendSidecarControlProtocol.SchemaVersion,
                response,
                DirectConnectCodeValidationResponse.Codec));
    }

    [Fact]
    public void SidecarEndpointStateUpdate_VectorMatchesWireContract()
    {
        var update = new SidecarEndpointStateUpdate(s_VectorGuid, ready: true, "ready");

        Assert.Equal(
            "c00003000100001a33221100554477668899aabbccddeeff01000000057265616479",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.EndpointStateUpdate,
                BackendSidecarControlProtocol.SchemaVersion,
                update,
                SidecarEndpointStateUpdate.Codec));
    }

    [Fact]
    public void SidecarEndpointStateAck_VectorMatchesWireContract()
    {
        var ack = SidecarEndpointStateAck.SuccessResult(s_VectorGuid);

        Assert.Equal(
            "c00004000100001533221100554477668899aabbccddeeff0100000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.EndpointStateAck,
                BackendSidecarControlProtocol.SchemaVersion,
                ack,
                SidecarEndpointStateAck.Codec));
    }

    [Fact]
    public void SidecarRuntimeStatusUpdate_VectorMatchesWireContract()
    {
        var update = new SidecarRuntimeStatusUpdate(
            s_VectorGuid,
            healthy: true,
            activeGatewaySessions: 2,
            activeChannels: 37,
            "steady");

        Assert.Equal(
            "c00005000100002333221100554477668899aabbccddeeff01000000020000002500000006737465616479",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.RuntimeStatusUpdate,
                BackendSidecarControlProtocol.SchemaVersion,
                update,
                SidecarRuntimeStatusUpdate.Codec));
    }

    [Fact]
    public void SidecarRuntimeStatusAck_VectorMatchesWireContract()
    {
        var ack = SidecarRuntimeStatusAck.SuccessResult(s_VectorGuid);

        Assert.Equal(
            "c00006000100001533221100554477668899aabbccddeeff0100000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.RuntimeStatusAck,
                BackendSidecarControlProtocol.SchemaVersion,
                ack,
                SidecarRuntimeStatusAck.Codec));
    }

    [Fact]
    public void SidecarShutdownStateUpdate_VectorMatchesWireContract()
    {
        var update = new SidecarShutdownStateUpdate(
            s_VectorGuid,
            shuttingDown: true,
            "rolling restart");

        Assert.Equal(
            "c00007000100002433221100554477668899aabbccddeeff010000000f726f6c6c696e672072657374617274",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ShutdownStateUpdate,
                BackendSidecarControlProtocol.SchemaVersion,
                update,
                SidecarShutdownStateUpdate.Codec));
    }

    [Fact]
    public void SidecarShutdownStateAck_VectorMatchesWireContract()
    {
        var ack = SidecarShutdownStateAck.SuccessResult(s_VectorGuid);

        Assert.Equal(
            "c00008000100001533221100554477668899aabbccddeeff0100000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ShutdownStateAck,
                BackendSidecarControlProtocol.SchemaVersion,
                ack,
                SidecarShutdownStateAck.Codec));
    }

    [Fact]
    public void SidecarManifestDeclarationUpdate_VectorMatchesWireContract()
    {
        var update = new SidecarManifestDeclarationUpdate(
            s_VectorGuid,
            "cpp-world:v2",
            s_ManifestHash);

        Assert.Equal(
            "c00009000100006433221100554477668899aabbccddeeff0000000c6370702d776f726c643a76320000004061663336396462326537316664353433616334343961656133366237303263616361623237393966653130313762656335306538346338363834353364613035",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ManifestDeclarationUpdate,
                BackendSidecarControlProtocol.SchemaVersion,
                update,
                SidecarManifestDeclarationUpdate.Codec));
    }

    [Fact]
    public void SidecarManifestDeclarationAck_VectorMatchesWireContract()
    {
        var ack = SidecarManifestDeclarationAck.SuccessResult(s_VectorGuid);

        Assert.Equal(
            "c0000a000100001533221100554477668899aabbccddeeff0100000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ManifestDeclarationAck,
                BackendSidecarControlProtocol.SchemaVersion,
                ack,
                SidecarManifestDeclarationAck.Codec));
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

    [Fact]
    public void SidecarManifestSnapshotResponseSuccess_VectorMatchesWireContract()
    {
        var response = SidecarManifestSnapshotResponse.SuccessResult(
            s_VectorGuid,
            CreateManifestSnapshot());

        Assert.Equal(
            "c0000c00010000a033221100554477668899aabbccddeeff010100000001000000096370702d776f726c640000000c6370702d776f726c643a7632000000406166333639646232653731666435343361633434396165613336623730326361636162323739396665313031376265633530653834633836383435336461303500000001010000650001010000000400000040000000000000000000000000019f2314e60000000000",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ManifestSnapshotResponse,
                BackendSidecarControlProtocol.SchemaVersion,
                response,
                SidecarManifestSnapshotResponse.Codec));
    }

    [Fact]
    public void SidecarManifestSnapshotResponseFailure_VectorMatchesWireContract()
    {
        var response = SidecarManifestSnapshotResponse.Failure(s_VectorGuid, "not loaded");

        Assert.Equal(
            "c0000c000100002033221100554477668899aabbccddeeff00000000000a6e6f74206c6f61646564",
            EncodeFrameHex(
                PacketKind.Control,
                BackendSidecarControlPacketIds.ManifestSnapshotResponse,
                BackendSidecarControlProtocol.SchemaVersion,
                response,
                SidecarManifestSnapshotResponse.Codec));
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

    private static NodeAuthChallenge CreateChallenge()
    {
        var nonce = new byte[MasterControlProtocol.AuthNonceLength];
        for (int index = 0; index < nonce.Length; index++)
        {
            nonce[index] = (byte)index;
        }

        return new NodeAuthChallenge("challenge-a", nonce);
    }

    private static BackendPacketManifestSnapshot CreateManifestSnapshot()
    {
        return new BackendPacketManifestSnapshot(
            [
                new BackendPacketManifest(
                    "cpp-world",
                    new BackendPacketManifestId("cpp-world:v2"),
                    [
                        new BackendPacketManifestEntry(
                            BackendPacketManifestDirection.ClientToBackend,
                            PacketKind.Request,
                            101,
                            1,
                            new BackendPacketPayloadConstraint(4, 64))
                    ])
            ],
            DateTimeOffset.FromUnixTimeMilliseconds(1_783_000_000_000));
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
