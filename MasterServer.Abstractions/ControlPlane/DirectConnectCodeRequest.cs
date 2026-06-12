using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCodeRequest
{
    public DirectConnectCodeRequest(
        Guid requestId,
        MasterNodeKind targetNodeKind,
        string targetMasterConnectionId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (targetNodeKind is not (MasterNodeKind.Dedicated or MasterNodeKind.Backend))
        {
            throw new ArgumentOutOfRangeException(nameof(targetNodeKind));
        }

        if (string.IsNullOrWhiteSpace(targetMasterConnectionId))
        {
            throw new ArgumentException("Target Master connection id is required.", nameof(targetMasterConnectionId));
        }

        RequestId = requestId;
        TargetNodeKind = targetNodeKind;
        TargetMasterConnectionId = targetMasterConnectionId;
    }

    public Guid RequestId { get; }

    public MasterNodeKind TargetNodeKind { get; }

    public string TargetMasterConnectionId { get; }

    public static IPacketCodec<DirectConnectCodeRequest> Codec { get; } = new DirectConnectCodeRequestCodec();

    private sealed class DirectConnectCodeRequestCodec : IPacketCodec<DirectConnectCodeRequest>
    {
        public int GetPayloadSize(DirectConnectCodeRequest value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.TargetMasterConnectionId);
        }

        public void Encode(DirectConnectCodeRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.TargetNodeKind);
            writer.WriteString(value.TargetMasterConnectionId);
        }

        public DirectConnectCodeRequest Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeRequest(
                reader.ReadGuid(),
                (MasterNodeKind)reader.ReadByte(),
                reader.ReadString());
        }
    }
}
