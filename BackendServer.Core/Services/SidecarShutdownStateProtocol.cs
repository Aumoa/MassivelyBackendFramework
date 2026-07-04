using PacketCore;

namespace BackendServer.Services;

internal sealed class SidecarShutdownStateUpdate
{
    public SidecarShutdownStateUpdate(Guid requestId, bool shuttingDown, string reason)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        ShuttingDown = shuttingDown;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public Guid RequestId { get; }

    public bool ShuttingDown { get; }

    public string Reason { get; }

    public static IPacketCodec<SidecarShutdownStateUpdate> Codec { get; } = new SidecarShutdownStateUpdateCodec();

    private sealed class SidecarShutdownStateUpdateCodec : IPacketCodec<SidecarShutdownStateUpdate>
    {
        public int GetPayloadSize(SidecarShutdownStateUpdate value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.Reason);
        }

        public void Encode(SidecarShutdownStateUpdate value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.ShuttingDown ? (byte)1 : (byte)0);
            writer.WriteString(value.Reason);
        }

        public SidecarShutdownStateUpdate Decode(ref PacketReader reader)
        {
            return new SidecarShutdownStateUpdate(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}

internal sealed class SidecarShutdownStateAck
{
    public SidecarShutdownStateAck(Guid requestId, bool success, string errorMessage)
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

    public static SidecarShutdownStateAck SuccessResult(Guid requestId)
    {
        return new SidecarShutdownStateAck(requestId, success: true, string.Empty);
    }

    public static IPacketCodec<SidecarShutdownStateAck> Codec { get; } = new SidecarShutdownStateAckCodec();

    private sealed class SidecarShutdownStateAckCodec : IPacketCodec<SidecarShutdownStateAck>
    {
        public int GetPayloadSize(SidecarShutdownStateAck value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(SidecarShutdownStateAck value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.ErrorMessage);
        }

        public SidecarShutdownStateAck Decode(ref PacketReader reader)
        {
            return new SidecarShutdownStateAck(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}
