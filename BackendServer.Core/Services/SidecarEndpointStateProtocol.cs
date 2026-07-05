using PacketCore;

namespace BackendServer.Services;

internal sealed class SidecarEndpointStateUpdate
{
    public SidecarEndpointStateUpdate(Guid requestId, bool ready, string detail)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Ready = ready;
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
    }

    public Guid RequestId { get; }

    public bool Ready { get; }

    public string Detail { get; }

    public static IPacketCodec<SidecarEndpointStateUpdate> Codec { get; } = new SidecarEndpointStateUpdateCodec();

    private sealed class SidecarEndpointStateUpdateCodec : IPacketCodec<SidecarEndpointStateUpdate>
    {
        public int GetPayloadSize(SidecarEndpointStateUpdate value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.Detail);
        }

        public void Encode(SidecarEndpointStateUpdate value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Ready ? (byte)1 : (byte)0);
            writer.WriteString(value.Detail);
        }

        public SidecarEndpointStateUpdate Decode(ref PacketReader reader)
        {
            return new SidecarEndpointStateUpdate(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}

internal sealed class SidecarEndpointStateAck
{
    public SidecarEndpointStateAck(Guid requestId, bool success, string errorMessage)
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

    public static SidecarEndpointStateAck SuccessResult(Guid requestId)
    {
        return new SidecarEndpointStateAck(requestId, success: true, string.Empty);
    }

    public static SidecarEndpointStateAck Failure(Guid requestId, string errorMessage)
    {
        return new SidecarEndpointStateAck(requestId, success: false, errorMessage);
    }

    public static IPacketCodec<SidecarEndpointStateAck> Codec { get; } = new SidecarEndpointStateAckCodec();

    private sealed class SidecarEndpointStateAckCodec : IPacketCodec<SidecarEndpointStateAck>
    {
        public int GetPayloadSize(SidecarEndpointStateAck value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(SidecarEndpointStateAck value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.ErrorMessage);
        }

        public SidecarEndpointStateAck Decode(ref PacketReader reader)
        {
            return new SidecarEndpointStateAck(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString());
        }
    }
}
