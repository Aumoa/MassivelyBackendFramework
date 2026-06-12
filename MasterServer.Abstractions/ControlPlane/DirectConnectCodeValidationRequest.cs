using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCodeValidationRequest
{
    public DirectConnectCodeValidationRequest(
        Guid requestId,
        string code,
        string gatewayNodeId,
        string gatewayMasterConnectionId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Direct connect code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        if (string.IsNullOrWhiteSpace(gatewayMasterConnectionId))
        {
            throw new ArgumentException("Gateway Master connection id is required.", nameof(gatewayMasterConnectionId));
        }

        RequestId = requestId;
        Code = code;
        GatewayNodeId = gatewayNodeId;
        GatewayMasterConnectionId = gatewayMasterConnectionId;
    }

    public Guid RequestId { get; }

    public string Code { get; }

    public string GatewayNodeId { get; }

    public string GatewayMasterConnectionId { get; }

    public static IPacketCodec<DirectConnectCodeValidationRequest> Codec { get; } = new DirectConnectCodeValidationRequestCodec();

    private sealed class DirectConnectCodeValidationRequestCodec : IPacketCodec<DirectConnectCodeValidationRequest>
    {
        public int GetPayloadSize(DirectConnectCodeValidationRequest value)
        {
            return sizeof(long) + sizeof(long) +
                   PacketWriter.GetStringSize(value.Code) +
                   PacketWriter.GetStringSize(value.GatewayNodeId) +
                   PacketWriter.GetStringSize(value.GatewayMasterConnectionId);
        }

        public void Encode(DirectConnectCodeValidationRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteString(value.Code);
            writer.WriteString(value.GatewayNodeId);
            writer.WriteString(value.GatewayMasterConnectionId);
        }

        public DirectConnectCodeValidationRequest Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeValidationRequest(
                reader.ReadGuid(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
