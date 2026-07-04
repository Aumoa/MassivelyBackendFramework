using MasterServer.ControlPlane;
using PacketCore;

namespace BackendServer.Services;

internal sealed class SidecarManifestSnapshotRequest
{
    public SidecarManifestSnapshotRequest(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
    }

    public Guid RequestId { get; }

    public static IPacketCodec<SidecarManifestSnapshotRequest> Codec { get; } = new SidecarManifestSnapshotRequestCodec();

    private sealed class SidecarManifestSnapshotRequestCodec : IPacketCodec<SidecarManifestSnapshotRequest>
    {
        public int GetPayloadSize(SidecarManifestSnapshotRequest value)
        {
            return sizeof(long) + sizeof(long);
        }

        public void Encode(SidecarManifestSnapshotRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
        }

        public SidecarManifestSnapshotRequest Decode(ref PacketReader reader)
        {
            return new SidecarManifestSnapshotRequest(reader.ReadGuid());
        }
    }
}

internal sealed class SidecarManifestSnapshotResponse
{
    public SidecarManifestSnapshotResponse(
        Guid requestId,
        bool success,
        BackendPacketManifestSnapshot? snapshot,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (success && snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        RequestId = requestId;
        Success = success;
        Snapshot = snapshot;
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public BackendPacketManifestSnapshot? Snapshot { get; }

    public string ErrorMessage { get; }

    public static SidecarManifestSnapshotResponse SuccessResult(
        Guid requestId,
        BackendPacketManifestSnapshot snapshot)
    {
        return new SidecarManifestSnapshotResponse(requestId, success: true, snapshot, string.Empty);
    }

    public static SidecarManifestSnapshotResponse Failure(Guid requestId, string errorMessage)
    {
        return new SidecarManifestSnapshotResponse(requestId, success: false, null, errorMessage);
    }

    public static IPacketCodec<SidecarManifestSnapshotResponse> Codec { get; } = new SidecarManifestSnapshotResponseCodec();

    private sealed class SidecarManifestSnapshotResponseCodec : IPacketCodec<SidecarManifestSnapshotResponse>
    {
        public int GetPayloadSize(SidecarManifestSnapshotResponse value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   sizeof(byte) +
                   (value.Snapshot == null ? 0 : BackendPacketManifestSnapshot.Codec.GetPayloadSize(value.Snapshot)) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(SidecarManifestSnapshotResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteByte(value.Snapshot == null ? (byte)0 : (byte)1);
            if (value.Snapshot != null)
            {
                BackendPacketManifestSnapshot.Codec.Encode(value.Snapshot, ref writer);
            }

            writer.WriteString(value.ErrorMessage);
        }

        public SidecarManifestSnapshotResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            var success = reader.ReadByte() != 0;
            var hasSnapshot = reader.ReadByte() != 0;
            var snapshot = hasSnapshot
                ? BackendPacketManifestSnapshot.Codec.Decode(ref reader)
                : null;
            return new SidecarManifestSnapshotResponse(
                requestId,
                success,
                snapshot,
                reader.ReadString());
        }
    }
}
