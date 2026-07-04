using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendServerListResponse
{
    private const int MaxEntryCount = 1024;

    public GatewayBackendServerListResponse(
        string backendKind,
        bool success,
        GatewayBackendServerListEntry[] entries,
        DateTimeOffset observedAt,
        string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (entries.Length > MaxEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        if (!success && entries.Length != 0)
        {
            throw new ArgumentException("Rejected server list responses must not include entries.", nameof(entries));
        }

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Rejected server list responses require an error message.", nameof(errorMessage));
        }

        BackendKind = backendKind.Trim();
        Success = success;
        Entries = entries;
        ObservedAt = observedAt;
        ErrorMessage = errorMessage;
    }

    public string BackendKind { get; }

    public bool Success { get; }

    public GatewayBackendServerListEntry[] Entries { get; }

    public DateTimeOffset ObservedAt { get; }

    public string ErrorMessage { get; }

    public static GatewayBackendServerListResponse Accepted(
        string backendKind,
        GatewayBackendServerListEntry[] entries,
        DateTimeOffset observedAt)
    {
        return new GatewayBackendServerListResponse(
            backendKind,
            true,
            entries,
            observedAt,
            string.Empty);
    }

    public static GatewayBackendServerListResponse Rejected(
        string backendKind,
        string errorMessage)
    {
        return new GatewayBackendServerListResponse(
            backendKind,
            false,
            Array.Empty<GatewayBackendServerListEntry>(),
            DateTimeOffset.UtcNow,
            errorMessage);
    }

    public static IPacketCodec<GatewayBackendServerListResponse> Codec { get; } = new GatewayBackendServerListResponseCodec();

    private sealed class GatewayBackendServerListResponseCodec : IPacketCodec<GatewayBackendServerListResponse>
    {
        public int GetPayloadSize(GatewayBackendServerListResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var size = PacketWriter.GetStringSize(value.BackendKind) +
                       sizeof(byte) +
                       sizeof(int) +
                       sizeof(long) +
                       PacketWriter.GetStringSize(value.ErrorMessage);
            foreach (var entry in value.Entries)
            {
                size += GatewayBackendServerListEntry.Codec.GetPayloadSize(entry);
            }

            return size;
        }

        public void Encode(GatewayBackendServerListResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteInt32(value.Entries.Length);
            foreach (var entry in value.Entries)
            {
                GatewayBackendServerListEntry.Codec.Encode(entry, ref writer);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
            writer.WriteString(value.ErrorMessage);
        }

        public GatewayBackendServerListResponse Decode(ref PacketReader reader)
        {
            var backendKind = reader.ReadString();
            var success = reader.ReadByte() != 0;
            var entryCount = reader.ReadInt32();
            if (entryCount < 0 || entryCount > MaxEntryCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Backend server list entry count.");
            }

            var entries = new GatewayBackendServerListEntry[entryCount];
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i] = GatewayBackendServerListEntry.Codec.Decode(ref reader);
            }

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            var errorMessage = reader.ReadString();
            return new GatewayBackendServerListResponse(
                backendKind,
                success,
                entries,
                observedAt,
                errorMessage);
        }
    }
}
