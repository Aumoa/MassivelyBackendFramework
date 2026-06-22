using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayBackendRoutePolicyManagementRequest
{
    public GatewayBackendRoutePolicyManagementRequest(
        Guid requestId,
        GatewayBackendRoutePolicyOperation operation,
        long entryId,
        string backendKind,
        bool enabled)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (!Enum.IsDefined(typeof(GatewayBackendRoutePolicyOperation), operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (RequiresEntryId(operation) && entryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryId));
        }

        RequestId = requestId;
        Operation = operation;
        EntryId = entryId;
        BackendKind = backendKind ?? throw new ArgumentNullException(nameof(backendKind));
        Enabled = enabled;
    }

    public Guid RequestId { get; }

    public GatewayBackendRoutePolicyOperation Operation { get; }

    public long EntryId { get; }

    public string BackendKind { get; }

    public bool Enabled { get; }

    public GatewayBackendRoutePolicyEntryInput ToInput()
    {
        return new GatewayBackendRoutePolicyEntryInput(BackendKind, Enabled);
    }

    public static GatewayBackendRoutePolicyManagementRequest List(Guid requestId)
    {
        return new GatewayBackendRoutePolicyManagementRequest(
            requestId,
            GatewayBackendRoutePolicyOperation.List,
            0,
            string.Empty,
            false);
    }

    public static GatewayBackendRoutePolicyManagementRequest Create(
        Guid requestId,
        GatewayBackendRoutePolicyEntryInput input)
    {
        return new GatewayBackendRoutePolicyManagementRequest(
            requestId,
            GatewayBackendRoutePolicyOperation.Create,
            0,
            input.BackendKind,
            input.Enabled);
    }

    public static GatewayBackendRoutePolicyManagementRequest Update(
        Guid requestId,
        long entryId,
        GatewayBackendRoutePolicyEntryInput input)
    {
        return new GatewayBackendRoutePolicyManagementRequest(
            requestId,
            GatewayBackendRoutePolicyOperation.Update,
            entryId,
            input.BackendKind,
            input.Enabled);
    }

    public static GatewayBackendRoutePolicyManagementRequest Remove(Guid requestId, long entryId)
    {
        return new GatewayBackendRoutePolicyManagementRequest(
            requestId,
            GatewayBackendRoutePolicyOperation.Remove,
            entryId,
            string.Empty,
            false);
    }

    public static IPacketCodec<GatewayBackendRoutePolicyManagementRequest> Codec { get; } = new GatewayBackendRoutePolicyManagementRequestCodec();

    private static bool RequiresEntryId(GatewayBackendRoutePolicyOperation operation)
    {
        return operation is
            GatewayBackendRoutePolicyOperation.Update or
            GatewayBackendRoutePolicyOperation.Remove;
    }

    private sealed class GatewayBackendRoutePolicyManagementRequestCodec : IPacketCodec<GatewayBackendRoutePolicyManagementRequest>
    {
        public int GetPayloadSize(GatewayBackendRoutePolicyManagementRequest value)
        {
            return 16 +
                   sizeof(byte) +
                   sizeof(long) +
                   PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(byte);
        }

        public void Encode(GatewayBackendRoutePolicyManagementRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Operation);
            writer.WriteInt64(value.EntryId);
            writer.WriteString(value.BackendKind);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
        }

        public GatewayBackendRoutePolicyManagementRequest Decode(ref PacketReader reader)
        {
            return new GatewayBackendRoutePolicyManagementRequest(
                reader.ReadGuid(),
                (GatewayBackendRoutePolicyOperation)reader.ReadByte(),
                reader.ReadInt64(),
                reader.ReadString(),
                reader.ReadByte() != 0);
        }
    }
}
