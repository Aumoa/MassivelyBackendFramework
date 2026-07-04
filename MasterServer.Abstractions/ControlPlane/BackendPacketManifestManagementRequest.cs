using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifestManagementRequest
{
    public BackendPacketManifestManagementRequest(
        Guid requestId,
        BackendPacketManifestOperation operation,
        long manifestRecordId,
        BackendPacketManifest? manifest,
        string auditNote)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (!Enum.IsDefined(typeof(BackendPacketManifestOperation), operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (RequiresRecordId(operation) && manifestRecordId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestRecordId));
        }

        if (RequiresManifest(operation) && manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        RequestId = requestId;
        Operation = operation;
        ManifestRecordId = manifestRecordId;
        Manifest = manifest;
        AuditNote = BackendPacketManifestInput.NormalizeAuditNote(auditNote);
    }

    public Guid RequestId { get; }

    public BackendPacketManifestOperation Operation { get; }

    public long ManifestRecordId { get; }

    public BackendPacketManifest? Manifest { get; }

    public string AuditNote { get; }

    public BackendPacketManifestInput ToInput()
    {
        return new BackendPacketManifestInput(
            Manifest ?? throw new InvalidOperationException("Backend packet manifest request does not contain a manifest."),
            AuditNote);
    }

    public static BackendPacketManifestManagementRequest List(Guid requestId)
    {
        return new BackendPacketManifestManagementRequest(
            requestId,
            BackendPacketManifestOperation.List,
            0,
            null,
            string.Empty);
    }

    public static BackendPacketManifestManagementRequest Create(
        Guid requestId,
        BackendPacketManifest manifest,
        string auditNote)
    {
        return new BackendPacketManifestManagementRequest(
            requestId,
            BackendPacketManifestOperation.Create,
            0,
            manifest,
            auditNote);
    }

    public static BackendPacketManifestManagementRequest Update(
        Guid requestId,
        long manifestRecordId,
        BackendPacketManifest manifest,
        string auditNote)
    {
        return new BackendPacketManifestManagementRequest(
            requestId,
            BackendPacketManifestOperation.Update,
            manifestRecordId,
            manifest,
            auditNote);
    }

    public static BackendPacketManifestManagementRequest Deprecate(
        Guid requestId,
        long manifestRecordId,
        string auditNote)
    {
        return new BackendPacketManifestManagementRequest(
            requestId,
            BackendPacketManifestOperation.Deprecate,
            manifestRecordId,
            null,
            auditNote);
    }

    public static BackendPacketManifestManagementRequest Remove(Guid requestId, long manifestRecordId)
    {
        return new BackendPacketManifestManagementRequest(
            requestId,
            BackendPacketManifestOperation.Remove,
            manifestRecordId,
            null,
            string.Empty);
    }

    public static IPacketCodec<BackendPacketManifestManagementRequest> Codec { get; } = new BackendPacketManifestManagementRequestCodec();

    private static bool RequiresRecordId(BackendPacketManifestOperation operation)
    {
        return operation is
            BackendPacketManifestOperation.Update or
            BackendPacketManifestOperation.Deprecate or
            BackendPacketManifestOperation.Remove;
    }

    private static bool RequiresManifest(BackendPacketManifestOperation operation)
    {
        return operation is
            BackendPacketManifestOperation.Create or
            BackendPacketManifestOperation.Update;
    }

    private sealed class BackendPacketManifestManagementRequestCodec : IPacketCodec<BackendPacketManifestManagementRequest>
    {
        public int GetPayloadSize(BackendPacketManifestManagementRequest value)
        {
            return 16 +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(byte) +
                   (value.Manifest == null ? 0 : BackendPacketManifestPacketCodec.GetManifestSize(value.Manifest)) +
                   PacketWriter.GetStringSize(value.AuditNote);
        }

        public void Encode(BackendPacketManifestManagementRequest value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Operation);
            writer.WriteInt64(value.ManifestRecordId);
            writer.WriteByte(value.Manifest == null ? (byte)0 : (byte)1);
            if (value.Manifest != null)
            {
                BackendPacketManifestPacketCodec.WriteManifest(value.Manifest, ref writer);
            }

            writer.WriteString(value.AuditNote);
        }

        public BackendPacketManifestManagementRequest Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            var operation = (BackendPacketManifestOperation)reader.ReadByte();
            var manifestRecordId = reader.ReadInt64();
            var hasManifest = reader.ReadByte() != 0;
            var manifest = hasManifest
                ? BackendPacketManifestPacketCodec.ReadManifest(ref reader)
                : null;
            var auditNote = reader.ReadString();
            return new BackendPacketManifestManagementRequest(
                requestId,
                operation,
                manifestRecordId,
                manifest,
                auditNote);
        }
    }
}
