using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class ServiceConnectionCredentialManagementResponse
{
    private const int MaxCredentialCount = 1024;

    public ServiceConnectionCredentialManagementResponse(
        Guid requestId,
        bool success,
        ServiceConnectionCredentialInfo[] credentials,
        string sharedSecret,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Success = success;
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        SharedSecret = sharedSecret ?? throw new ArgumentNullException(nameof(sharedSecret));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public ServiceConnectionCredentialInfo[] Credentials { get; }

    public string SharedSecret { get; }

    public string ErrorMessage { get; }

    public static ServiceConnectionCredentialManagementResponse SuccessResult(
        Guid requestId,
        ServiceConnectionCredentialInfo[] credentials,
        string sharedSecret = "")
    {
        return new ServiceConnectionCredentialManagementResponse(
            requestId,
            true,
            credentials,
            sharedSecret,
            string.Empty);
    }

    public static ServiceConnectionCredentialManagementResponse Failure(Guid requestId, string errorMessage)
    {
        return new ServiceConnectionCredentialManagementResponse(
            requestId,
            false,
            Array.Empty<ServiceConnectionCredentialInfo>(),
            string.Empty,
            errorMessage);
    }

    public static IPacketCodec<ServiceConnectionCredentialManagementResponse> Codec { get; } = new ServiceConnectionCredentialManagementResponseCodec();

    private sealed class ServiceConnectionCredentialManagementResponseCodec : IPacketCodec<ServiceConnectionCredentialManagementResponse>
    {
        public int GetPayloadSize(ServiceConnectionCredentialManagementResponse value)
        {
            int size = 16 +
                       sizeof(byte) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.SharedSecret) +
                       PacketWriter.GetStringSize(value.ErrorMessage);

            foreach (var credential in value.Credentials)
            {
                size += GetCredentialSize(credential);
            }

            return size;
        }

        public void Encode(ServiceConnectionCredentialManagementResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Credentials.Length);
            foreach (var credential in value.Credentials)
            {
                WriteCredential(credential, ref writer);
            }

            writer.WriteString(value.SharedSecret);
            writer.WriteString(value.ErrorMessage);
        }

        public ServiceConnectionCredentialManagementResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            bool success = reader.ReadByte() != 0;
            int credentialCount = reader.ReadInt32();
            if (credentialCount < 0 || credentialCount > MaxCredentialCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid service connection credential count.");
            }

            var credentials = new ServiceConnectionCredentialInfo[credentialCount];
            for (int i = 0; i < credentials.Length; i++)
            {
                credentials[i] = ReadCredential(ref reader);
            }

            string sharedSecret = reader.ReadString();
            string errorMessage = reader.ReadString();
            return new ServiceConnectionCredentialManagementResponse(
                requestId,
                success,
                credentials,
                sharedSecret,
                errorMessage);
        }

        private static int GetCredentialSize(ServiceConnectionCredentialInfo value)
        {
            return sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.NodeId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.BackendKind ?? string.Empty) +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(long);
        }

        private static void WriteCredential(ServiceConnectionCredentialInfo value, ref PacketWriter writer)
        {
            writer.WriteInt64(value.Id);
            writer.WriteByte((byte)value.NodeKind);
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.BackendKind ?? string.Empty);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
            writer.WriteInt64(value.CreatedAt.Ticks);
            writer.WriteInt64(value.UpdatedAt.Ticks);
        }

        private static ServiceConnectionCredentialInfo ReadCredential(ref PacketReader reader)
        {
            return new ServiceConnectionCredentialInfo(
                reader.ReadInt64(),
                (MasterNodeKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                ToOptionalString(reader.ReadString()),
                reader.ReadByte() != 0,
                new DateTime(reader.ReadInt64()),
                new DateTime(reader.ReadInt64()));
        }

        private static string? ToOptionalString(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
