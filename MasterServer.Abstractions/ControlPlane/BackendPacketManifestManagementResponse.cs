using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifestManagementResponse
{
    public BackendPacketManifestManagementResponse(
        Guid requestId,
        bool success,
        BackendPacketManifestInfo[] manifests,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Success = success;
        Manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public BackendPacketManifestInfo[] Manifests { get; }

    public string ErrorMessage { get; }

    public static BackendPacketManifestManagementResponse SuccessResult(
        Guid requestId,
        BackendPacketManifestInfo[] manifests)
    {
        return new BackendPacketManifestManagementResponse(
            requestId,
            true,
            manifests,
            string.Empty);
    }

    public static BackendPacketManifestManagementResponse Failure(Guid requestId, string errorMessage)
    {
        return new BackendPacketManifestManagementResponse(
            requestId,
            false,
            Array.Empty<BackendPacketManifestInfo>(),
            errorMessage);
    }

    public static IPacketCodec<BackendPacketManifestManagementResponse> Codec { get; } = new BackendPacketManifestManagementResponseCodec();

    private sealed class BackendPacketManifestManagementResponseCodec : IPacketCodec<BackendPacketManifestManagementResponse>
    {
        public int GetPayloadSize(BackendPacketManifestManagementResponse value)
        {
            int size = 16 +
                       sizeof(byte) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.ErrorMessage);
            foreach (var manifest in value.Manifests)
            {
                size += BackendPacketManifestPacketCodec.GetInfoSize(manifest);
            }

            return size;
        }

        public void Encode(BackendPacketManifestManagementResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Manifests.Length);
            foreach (var manifest in value.Manifests)
            {
                BackendPacketManifestPacketCodec.WriteInfo(manifest, ref writer);
            }

            writer.WriteString(value.ErrorMessage);
        }

        public BackendPacketManifestManagementResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            var success = reader.ReadByte() != 0;
            var manifestCount = reader.ReadInt32();
            if (manifestCount < 0 || manifestCount > BackendPacketManifestPacketCodec.MaxManifestCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Backend packet manifest info count.");
            }

            var manifests = new BackendPacketManifestInfo[manifestCount];
            for (int i = 0; i < manifests.Length; i++)
            {
                manifests[i] = BackendPacketManifestPacketCodec.ReadInfo(ref reader);
            }

            var errorMessage = reader.ReadString();
            return new BackendPacketManifestManagementResponse(
                requestId,
                success,
                manifests,
                errorMessage);
        }
    }
}
