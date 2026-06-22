using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayClientSecretCredentialManagementRequest
{
    public GatewayClientSecretCredentialManagementRequest(
        Guid requestId,
        GatewayClientSecretCredentialOperation operation,
        long credentialId,
        string subjectId,
        string displayName,
        bool enabled)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (!Enum.IsDefined(typeof(GatewayClientSecretCredentialOperation), operation))
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
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Enabled = enabled;
    }

    public Guid RequestId { get; }

    public GatewayClientSecretCredentialOperation Operation { get; }

    public long CredentialId { get; }

    public string SubjectId { get; }

    public string DisplayName { get; }

    public bool Enabled { get; }

    public GatewayClientSecretCredentialInput ToInput()
    {
        return new GatewayClientSecretCredentialInput(
            SubjectId,
            DisplayName,
            Enabled);
    }

    public static GatewayClientSecretCredentialManagementRequest List(Guid requestId)
    {
        return new GatewayClientSecretCredentialManagementRequest(
            requestId,
            GatewayClientSecretCredentialOperation.List,
            0,
            string.Empty,
            string.Empty,
            false);
    }

    public static GatewayClientSecretCredentialManagementRequest Create(
        Guid requestId,
        GatewayClientSecretCredentialInput input)
    {
        return new GatewayClientSecretCredentialManagementRequest(
            requestId,
            GatewayClientSecretCredentialOperation.Create,
            0,
            input.SubjectId,
            input.DisplayName,
            input.Enabled);
    }

    public static GatewayClientSecretCredentialManagementRequest Update(
        Guid requestId,
        long credentialId,
        GatewayClientSecretCredentialInput input)
    {
        return new GatewayClientSecretCredentialManagementRequest(
            requestId,
            GatewayClientSecretCredentialOperation.Update,
            credentialId,
            input.SubjectId,
            input.DisplayName,
            input.Enabled);
    }

    public static GatewayClientSecretCredentialManagementRequest RotateSecret(Guid requestId, long credentialId)
    {
        return new GatewayClientSecretCredentialManagementRequest(
            requestId,
            GatewayClientSecretCredentialOperation.RotateSecret,
            credentialId,
            string.Empty,
            string.Empty,
            false);
    }

    public static GatewayClientSecretCredentialManagementRequest Remove(Guid requestId, long credentialId)
    {
        return new GatewayClientSecretCredentialManagementRequest(
            requestId,
            GatewayClientSecretCredentialOperation.Remove,
            credentialId,
            string.Empty,
            string.Empty,
            false);
    }

    public static IPacketCodec<GatewayClientSecretCredentialManagementRequest> Codec { get; } = new GatewayClientSecretCredentialManagementRequestCodec();

    private static bool RequiresCredentialId(GatewayClientSecretCredentialOperation operation)
    {
        return operation is
            GatewayClientSecretCredentialOperation.Update or
            GatewayClientSecretCredentialOperation.RotateSecret or
            GatewayClientSecretCredentialOperation.Remove;
    }

    private sealed class GatewayClientSecretCredentialManagementRequestCodec : IPacketCodec<GatewayClientSecretCredentialManagementRequest>
    {
        public int GetPayloadSize(GatewayClientSecretCredentialManagementRequest value)
        {
            return 16 +
                   sizeof(byte) +
                   sizeof(long) +
                   PacketWriter.GetStringSize(value.SubjectId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   sizeof(byte);
        }

        public void Encode(GatewayClientSecretCredentialManagementRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Operation);
            writer.WriteInt64(value.CredentialId);
            writer.WriteString(value.SubjectId);
            writer.WriteString(value.DisplayName);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
        }

        public GatewayClientSecretCredentialManagementRequest Decode(ref PacketReader reader)
        {
            return new GatewayClientSecretCredentialManagementRequest(
                reader.ReadGuid(),
                (GatewayClientSecretCredentialOperation)reader.ReadByte(),
                reader.ReadInt64(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadByte() != 0);
        }
    }
}
