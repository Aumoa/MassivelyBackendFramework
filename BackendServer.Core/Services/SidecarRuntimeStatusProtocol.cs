using PacketCore;

namespace BackendServer.Services;

internal sealed class SidecarRuntimeStatusUpdate
{
    public SidecarRuntimeStatusUpdate(
        Guid requestId,
        bool healthy,
        int activeGatewaySessions,
        int activeChannels,
        string detail)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (activeGatewaySessions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeGatewaySessions));
        }

        if (activeChannels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeChannels));
        }

        RequestId = requestId;
        Healthy = healthy;
        ActiveGatewaySessions = activeGatewaySessions;
        ActiveChannels = activeChannels;
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
    }

    public Guid RequestId { get; }

    public bool Healthy { get; }

    public int ActiveGatewaySessions { get; }

    public int ActiveChannels { get; }

    public string Detail { get; }

    public static IPacketCodec<SidecarRuntimeStatusUpdate> Codec { get; } = new SidecarRuntimeStatusUpdateCodec();

    private sealed class SidecarRuntimeStatusUpdateCodec : IPacketCodec<SidecarRuntimeStatusUpdate>
    {
        public int GetPayloadSize(SidecarRuntimeStatusUpdate value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   sizeof(int) +
                   sizeof(int) +
                   PacketWriter.GetStringSize(value.Detail);
        }

        public void Encode(SidecarRuntimeStatusUpdate value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Healthy ? (byte)1 : (byte)0);
            writer.WriteInt32(value.ActiveGatewaySessions);
            writer.WriteInt32(value.ActiveChannels);
            writer.WriteString(value.Detail);
        }

        public SidecarRuntimeStatusUpdate Decode(ref PacketReader reader)
        {
            return new SidecarRuntimeStatusUpdate(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString());
        }
    }
}

internal sealed class SidecarRuntimeStatusAck
{
    public SidecarRuntimeStatusAck(Guid requestId, bool success, string errorMessage)
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

    public static SidecarRuntimeStatusAck SuccessResult(Guid requestId)
    {
        return new SidecarRuntimeStatusAck(requestId, success: true, string.Empty);
    }

    public static IPacketCodec<SidecarRuntimeStatusAck> Codec { get; } = new SidecarRuntimeStatusAckCodec();

    private sealed class SidecarRuntimeStatusAckCodec : IPacketCodec<SidecarRuntimeStatusAck>
    {
        public int GetPayloadSize(SidecarRuntimeStatusAck value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(SidecarRuntimeStatusAck value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.ErrorMessage);
        }

        public SidecarRuntimeStatusAck Decode(ref PacketReader reader)
        {
            return new SidecarRuntimeStatusAck(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}
