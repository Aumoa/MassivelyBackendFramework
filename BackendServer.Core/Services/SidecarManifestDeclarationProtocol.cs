using PacketCore;

namespace BackendServer.Services;

internal sealed class SidecarManifestDeclarationUpdate
{
    public SidecarManifestDeclarationUpdate(
        Guid requestId,
        string manifestId,
        string manifestHash)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        ManifestId = manifestId ?? throw new ArgumentNullException(nameof(manifestId));
        ManifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
    }

    public Guid RequestId { get; }

    public string ManifestId { get; }

    public string ManifestHash { get; }

    public static IPacketCodec<SidecarManifestDeclarationUpdate> Codec { get; } = new SidecarManifestDeclarationUpdateCodec();

    private sealed class SidecarManifestDeclarationUpdateCodec : IPacketCodec<SidecarManifestDeclarationUpdate>
    {
        public int GetPayloadSize(SidecarManifestDeclarationUpdate value)
        {
            return sizeof(long) + sizeof(long) +
                   PacketWriter.GetStringSize(value.ManifestId) +
                   PacketWriter.GetStringSize(value.ManifestHash);
        }

        public void Encode(SidecarManifestDeclarationUpdate value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteString(value.ManifestId);
            writer.WriteString(value.ManifestHash);
        }

        public SidecarManifestDeclarationUpdate Decode(ref PacketReader reader)
        {
            return new SidecarManifestDeclarationUpdate(
                reader.ReadGuid(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}

internal sealed class SidecarManifestDeclarationAck
{
    public SidecarManifestDeclarationAck(Guid requestId, bool success, string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Success = success;
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public string ErrorMessage { get; }

    public static SidecarManifestDeclarationAck SuccessResult(Guid requestId)
    {
        return new SidecarManifestDeclarationAck(requestId, success: true, string.Empty);
    }

    public static SidecarManifestDeclarationAck Failure(Guid requestId, string errorMessage)
    {
        return new SidecarManifestDeclarationAck(requestId, success: false, errorMessage);
    }

    public static IPacketCodec<SidecarManifestDeclarationAck> Codec { get; } = new SidecarManifestDeclarationAckCodec();

    private sealed class SidecarManifestDeclarationAckCodec : IPacketCodec<SidecarManifestDeclarationAck>
    {
        public int GetPayloadSize(SidecarManifestDeclarationAck value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(SidecarManifestDeclarationAck value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.ErrorMessage);
        }

        public SidecarManifestDeclarationAck Decode(ref PacketReader reader)
        {
            return new SidecarManifestDeclarationAck(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}
