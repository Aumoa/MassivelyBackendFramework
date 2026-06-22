using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayBackendRoutePolicyManagementResponse
{
    private const int MaxEntryCount = 2048;

    public GatewayBackendRoutePolicyManagementResponse(
        Guid requestId,
        bool success,
        GatewayBackendRoutePolicyEntryInfo[] entries,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        RequestId = requestId;
        Success = success;
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public GatewayBackendRoutePolicyEntryInfo[] Entries { get; }

    public string ErrorMessage { get; }

    public static GatewayBackendRoutePolicyManagementResponse SuccessResult(
        Guid requestId,
        GatewayBackendRoutePolicyEntryInfo[] entries)
    {
        return new GatewayBackendRoutePolicyManagementResponse(
            requestId,
            true,
            entries,
            string.Empty);
    }

    public static GatewayBackendRoutePolicyManagementResponse Failure(Guid requestId, string errorMessage)
    {
        return new GatewayBackendRoutePolicyManagementResponse(
            requestId,
            false,
            Array.Empty<GatewayBackendRoutePolicyEntryInfo>(),
            errorMessage);
    }

    public static IPacketCodec<GatewayBackendRoutePolicyManagementResponse> Codec { get; } = new GatewayBackendRoutePolicyManagementResponseCodec();

    private sealed class GatewayBackendRoutePolicyManagementResponseCodec : IPacketCodec<GatewayBackendRoutePolicyManagementResponse>
    {
        public int GetPayloadSize(GatewayBackendRoutePolicyManagementResponse value)
        {
            int size = 16 +
                       sizeof(byte) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.ErrorMessage);

            foreach (var entry in value.Entries)
            {
                size += GetEntrySize(entry);
            }

            return size;
        }

        public void Encode(GatewayBackendRoutePolicyManagementResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Entries.Length);
            foreach (var entry in value.Entries)
            {
                WriteEntry(entry, ref writer);
            }

            writer.WriteString(value.ErrorMessage);
        }

        public GatewayBackendRoutePolicyManagementResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            bool success = reader.ReadByte() != 0;
            int entryCount = reader.ReadInt32();
            if (entryCount < 0 || entryCount > MaxEntryCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway Backend route policy entry count.");
            }

            var entries = new GatewayBackendRoutePolicyEntryInfo[entryCount];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = ReadEntry(ref reader);
            }

            string errorMessage = reader.ReadString();
            return new GatewayBackendRoutePolicyManagementResponse(
                requestId,
                success,
                entries,
                errorMessage);
        }

        private static int GetEntrySize(GatewayBackendRoutePolicyEntryInfo value)
        {
            return sizeof(long) +
                   PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(long);
        }

        private static void WriteEntry(GatewayBackendRoutePolicyEntryInfo value, ref PacketWriter writer)
        {
            writer.WriteInt64(value.Id);
            writer.WriteString(value.BackendKind);
            writer.WriteByte(value.Enabled ? (byte)1 : (byte)0);
            writer.WriteInt64(value.CreatedAt.Ticks);
            writer.WriteInt64(value.UpdatedAt.Ticks);
        }

        private static GatewayBackendRoutePolicyEntryInfo ReadEntry(ref PacketReader reader)
        {
            return new GatewayBackendRoutePolicyEntryInfo(
                reader.ReadInt64(),
                reader.ReadString(),
                reader.ReadByte() != 0,
                new DateTime(reader.ReadInt64()),
                new DateTime(reader.ReadInt64()));
        }
    }
}
