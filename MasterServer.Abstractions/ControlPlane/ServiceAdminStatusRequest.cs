using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class ServiceAdminStatusRequest
{
    public ServiceAdminStatusRequest(Guid requestId, string targetConnectionId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(targetConnectionId))
        {
            throw new ArgumentException("Target connection id is required.", nameof(targetConnectionId));
        }

        RequestId = requestId;
        TargetConnectionId = targetConnectionId;
    }

    public Guid RequestId { get; }

    public string TargetConnectionId { get; }

    public static IPacketCodec<ServiceAdminStatusRequest> Codec { get; } = new ServiceAdminStatusRequestCodec();

    private sealed class ServiceAdminStatusRequestCodec : IPacketCodec<ServiceAdminStatusRequest>
    {
        public int GetPayloadSize(ServiceAdminStatusRequest value)
        {
            return 16 + PacketWriter.GetStringSize(value.TargetConnectionId);
        }

        public void Encode(ServiceAdminStatusRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteString(value.TargetConnectionId);
        }

        public ServiceAdminStatusRequest Decode(ref PacketReader reader)
        {
            return new ServiceAdminStatusRequest(reader.ReadGuid(), reader.ReadString());
        }
    }
}
