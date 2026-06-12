using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class ServiceConnectionCredentialManagementRequest
{
    public ServiceConnectionCredentialManagementRequest(
        Guid requestId,
        ServiceConnectionCredentialOperation operation,
        long credentialId,
        MasterNodeKind nodeKind,
        string nodeId,
        string displayName,
        string? backendKind,
        bool enabled)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (!Enum.IsDefined(typeof(ServiceConnectionCredentialOperation), operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (RequiresCredentialId(operation) && credentialId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialId));
        }

        RequestId = requestId;
        Operation = operation;
        CredentialId = credentialId;
        NodeKind = nodeKind;
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        BackendKind = backendKind;
        Enabled = enabled;
    }

    public Guid RequestId { get; }

    public ServiceConnectionCredentialOperation Operation { get; }

    public long CredentialId { get; }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string? BackendKind { get; }

    public bool Enabled { get; }

    public ServiceConnectionCredentialInput ToInput()
    {
        return new ServiceConnectionCredentialInput(
            NodeKind,
            NodeId,
            DisplayName,
            string.IsNullOrWhiteSpace(BackendKind) ? null : BackendKind,
            Enabled);
    }

    public static ServiceConnectionCredentialManagementRequest List(Guid requestId)
    {
        return new ServiceConnectionCredentialManagementRequest(
            requestId,
            ServiceConnectionCredentialOperation.List,
            0,
            MasterNodeKind.Unknown,
            string.Empty,
            string.Empty,
            null,
            false);
    }

    public static ServiceConnectionCredentialManagementRequest Create(
        Guid requestId,
        ServiceConnectionCredentialInput input)
    {
        return new ServiceConnectionCredentialManagementRequest(
            requestId,
            ServiceConnectionCredentialOperation.Create,
            0,
            input.NodeKind,
            input.NodeId,
            input.DisplayName,
            input.BackendKind,
            input.Enabled);
    }

    public static ServiceConnectionCredentialManagementRequest Update(
        Guid requestId,
        long credentialId,
        ServiceConnectionCredentialInput input)
    {
        return new ServiceConnectionCredentialManagementRequest(
            requestId,
            ServiceConnectionCredentialOperation.Update,
            credentialId,
            input.NodeKind,
            input.NodeId,
            input.DisplayName,
            input.BackendKind,
            input.Enabled);
    }

    public static ServiceConnectionCredentialManagementRequest RotateSecret(Guid requestId, long credentialId)
    {
        return new ServiceConnectionCredentialManagementRequest(
            requestId,
            ServiceConnectionCredentialOperation.RotateSecret,
            credentialId,
            MasterNodeKind.Unknown,
            string.Empty,
            string.Empty,
            null,
            false);
    }

    public static ServiceConnectionCredentialManagementRequest Remove(Guid requestId, long credentialId)
    {
        return new ServiceConnectionCredentialManagementRequest(
            requestId,
            ServiceConnectionCredentialOperation.Remove,
            credentialId,
            MasterNodeKind.Unknown,
            string.Empty,
            string.Empty,
            null,
            false);
    }

    public static IPacketCodec<ServiceConnectionCredentialManagementRequest> Codec { get; } = new ServiceConnectionCredentialManagementRequestCodec();

    private static bool RequiresCredentialId(ServiceConnectionCredentialOperation operation)
    {
        return operation is
            ServiceConnectionCredentialOperation.Update or
            ServiceConnectionCredentialOperation.RotateSecret or
            ServiceConnectionCredentialOperation.Remove;
    }

    private sealed class ServiceConnectionCredentialManagementRequestCodec : IPacketCodec<ServiceConnectionCredentialManagementRequest>
    {
        public int GetPayloadSize(ServiceConnectionCredentialManagementRequest value)
        {
            return 16 +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.NodeId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.BackendKind ?? string.Empty) +
                   sizeof(byte);
        }

        public void Encode(ServiceConnectionCredentialManagementRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Operation);
            writer.WriteInt64(value.CredentialId);
            writer.WriteByte((byte)value.NodeKind);
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.BackendKind ?? string.Empty);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
        }

        public ServiceConnectionCredentialManagementRequest Decode(ref PacketReader reader)
        {
            return new ServiceConnectionCredentialManagementRequest(
                reader.ReadGuid(),
                (ServiceConnectionCredentialOperation)reader.ReadByte(),
                reader.ReadInt64(),
                (MasterNodeKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                ToOptionalString(reader.ReadString()),
                reader.ReadByte() != 0);
        }

        private static string? ToOptionalString(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
