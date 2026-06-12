using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCodeValidationResponse
{
    public DirectConnectCodeValidationResponse(
        Guid requestId,
        bool success,
        string gatewayNodeId,
        string gatewayMasterConnectionId,
        MasterNodeKind targetNodeKind,
        string targetNodeId,
        string targetMasterConnectionId,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (gatewayNodeId == null)
        {
            throw new ArgumentNullException(nameof(gatewayNodeId));
        }

        if (gatewayMasterConnectionId == null)
        {
            throw new ArgumentNullException(nameof(gatewayMasterConnectionId));
        }

        if (targetNodeKind is not (MasterNodeKind.Dedicated or MasterNodeKind.Backend or MasterNodeKind.Unknown))
        {
            throw new ArgumentOutOfRangeException(nameof(targetNodeKind));
        }

        if (targetNodeId == null)
        {
            throw new ArgumentNullException(nameof(targetNodeId));
        }

        if (targetMasterConnectionId == null)
        {
            throw new ArgumentNullException(nameof(targetMasterConnectionId));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        RequestId = requestId;
        Success = success;
        GatewayNodeId = gatewayNodeId;
        GatewayMasterConnectionId = gatewayMasterConnectionId;
        TargetNodeKind = targetNodeKind;
        TargetNodeId = targetNodeId;
        TargetMasterConnectionId = targetMasterConnectionId;
        ErrorMessage = errorMessage;
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public string GatewayNodeId { get; }

    public string GatewayMasterConnectionId { get; }

    public MasterNodeKind TargetNodeKind { get; }

    public string TargetNodeId { get; }

    public string TargetMasterConnectionId { get; }

    public string ErrorMessage { get; }

    public static DirectConnectCodeValidationResponse Failure(Guid requestId, string errorMessage)
    {
        return new DirectConnectCodeValidationResponse(
            requestId,
            false,
            string.Empty,
            string.Empty,
            MasterNodeKind.Unknown,
            string.Empty,
            string.Empty,
            errorMessage);
    }

    public static IPacketCodec<DirectConnectCodeValidationResponse> Codec { get; } = new DirectConnectCodeValidationResponseCodec();

    private sealed class DirectConnectCodeValidationResponseCodec : IPacketCodec<DirectConnectCodeValidationResponse>
    {
        public int GetPayloadSize(DirectConnectCodeValidationResponse value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.GatewayNodeId) +
                   PacketWriter.GetStringSize(value.GatewayMasterConnectionId) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.TargetNodeId) +
                   PacketWriter.GetStringSize(value.TargetMasterConnectionId) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(DirectConnectCodeValidationResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.GatewayNodeId);
            writer.WriteString(value.GatewayMasterConnectionId);
            writer.WriteByte((byte)value.TargetNodeKind);
            writer.WriteString(value.TargetNodeId);
            writer.WriteString(value.TargetMasterConnectionId);
            writer.WriteString(value.ErrorMessage);
        }

        public DirectConnectCodeValidationResponse Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeValidationResponse(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString(),
                reader.ReadString(),
                (MasterNodeKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
