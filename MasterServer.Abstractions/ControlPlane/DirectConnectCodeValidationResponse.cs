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
        string dedicatedMasterConnectionId,
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

        if (dedicatedMasterConnectionId == null)
        {
            throw new ArgumentNullException(nameof(dedicatedMasterConnectionId));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        RequestId = requestId;
        Success = success;
        GatewayNodeId = gatewayNodeId;
        GatewayMasterConnectionId = gatewayMasterConnectionId;
        DedicatedMasterConnectionId = dedicatedMasterConnectionId;
        ErrorMessage = errorMessage;
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public string GatewayNodeId { get; }

    public string GatewayMasterConnectionId { get; }

    public string DedicatedMasterConnectionId { get; }

    public string ErrorMessage { get; }

    public static DirectConnectCodeValidationResponse Failure(Guid requestId, string errorMessage)
    {
        return new DirectConnectCodeValidationResponse(
            requestId,
            false,
            string.Empty,
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
                   PacketWriter.GetStringSize(value.DedicatedMasterConnectionId) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(DirectConnectCodeValidationResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.GatewayNodeId);
            writer.WriteString(value.GatewayMasterConnectionId);
            writer.WriteString(value.DedicatedMasterConnectionId);
            writer.WriteString(value.ErrorMessage);
        }

        public DirectConnectCodeValidationResponse Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeValidationResponse(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
