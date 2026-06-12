using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCodeRequest
{
    public DirectConnectCodeRequest(Guid requestId, string dedicatedMasterConnectionId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(dedicatedMasterConnectionId))
        {
            throw new ArgumentException("Dedicated Master connection id is required.", nameof(dedicatedMasterConnectionId));
        }

        RequestId = requestId;
        DedicatedMasterConnectionId = dedicatedMasterConnectionId;
    }

    public Guid RequestId { get; }

    public string DedicatedMasterConnectionId { get; }

    public static IPacketCodec<DirectConnectCodeRequest> Codec { get; } = new DirectConnectCodeRequestCodec();

    private sealed class DirectConnectCodeRequestCodec : IPacketCodec<DirectConnectCodeRequest>
    {
        public int GetPayloadSize(DirectConnectCodeRequest value)
        {
            return sizeof(long) + sizeof(long) +
                   PacketWriter.GetStringSize(value.DedicatedMasterConnectionId);
        }

        public void Encode(DirectConnectCodeRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteString(value.DedicatedMasterConnectionId);
        }

        public DirectConnectCodeRequest Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeRequest(reader.ReadGuid(), reader.ReadString());
        }
    }
}
