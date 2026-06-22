using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayClientSecretCredentialManagementResponse
{
    private const int MaxCredentialCount = 2048;

    public GatewayClientSecretCredentialManagementResponse(
        Guid requestId,
        bool success,
        GatewayClientSecretCredentialInfo[] credentials,
        string accessToken,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Success = success;
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public GatewayClientSecretCredentialInfo[] Credentials { get; }

    public string AccessToken { get; }

    public string ErrorMessage { get; }

    public static GatewayClientSecretCredentialManagementResponse SuccessResult(
        Guid requestId,
        GatewayClientSecretCredentialInfo[] credentials,
        string accessToken = "")
    {
        return new GatewayClientSecretCredentialManagementResponse(
            requestId,
            true,
            credentials,
            accessToken,
            string.Empty);
    }

    public static GatewayClientSecretCredentialManagementResponse Failure(Guid requestId, string errorMessage)
    {
        return new GatewayClientSecretCredentialManagementResponse(
            requestId,
            false,
            Array.Empty<GatewayClientSecretCredentialInfo>(),
            string.Empty,
            errorMessage);
    }

    public static IPacketCodec<GatewayClientSecretCredentialManagementResponse> Codec { get; } = new GatewayClientSecretCredentialManagementResponseCodec();

    private sealed class GatewayClientSecretCredentialManagementResponseCodec : IPacketCodec<GatewayClientSecretCredentialManagementResponse>
    {
        public int GetPayloadSize(GatewayClientSecretCredentialManagementResponse value)
        {
            int size = 16 +
                       sizeof(byte) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.AccessToken) +
                       PacketWriter.GetStringSize(value.ErrorMessage);

            foreach (var credential in value.Credentials)
            {
                size += GetCredentialSize(credential);
            }

            return size;
        }

        public void Encode(GatewayClientSecretCredentialManagementResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Credentials.Length);
            foreach (var credential in value.Credentials)
            {
                WriteCredential(credential, ref writer);
            }

            writer.WriteString(value.AccessToken);
            writer.WriteString(value.ErrorMessage);
        }

        public GatewayClientSecretCredentialManagementResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            bool success = reader.ReadByte() != 0;
            int credentialCount = reader.ReadInt32();
            if (credentialCount < 0 || credentialCount > MaxCredentialCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway client secret credential count.");
            }

            var credentials = new GatewayClientSecretCredentialInfo[credentialCount];
            for (int i = 0; i < credentials.Length; i++)
            {
                credentials[i] = ReadCredential(ref reader);
            }

            string accessToken = reader.ReadString();
            string errorMessage = reader.ReadString();
            return new GatewayClientSecretCredentialManagementResponse(
                requestId,
                success,
                credentials,
                accessToken,
                errorMessage);
        }

        private static int GetCredentialSize(GatewayClientSecretCredentialInfo value)
        {
            return sizeof(long) +
                   PacketWriter.GetStringSize(value.TokenId) +
                   PacketWriter.GetStringSize(value.SubjectId) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(long);
        }

        private static void WriteCredential(GatewayClientSecretCredentialInfo value, ref PacketWriter writer)
        {
            writer.WriteInt64(value.Id);
            writer.WriteString(value.TokenId);
            writer.WriteString(value.SubjectId);
            writer.WriteString(value.DisplayName);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
            writer.WriteInt64(value.CreatedAt.Ticks);
            writer.WriteInt64(value.UpdatedAt.Ticks);
        }

        private static GatewayClientSecretCredentialInfo ReadCredential(ref PacketReader reader)
        {
            return new GatewayClientSecretCredentialInfo(
                reader.ReadInt64(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadByte() != 0,
                new DateTime(reader.ReadInt64()),
                new DateTime(reader.ReadInt64()));
        }
    }
}
