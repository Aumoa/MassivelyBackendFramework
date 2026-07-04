using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

internal static class BackendPacketManifestPacketCodec
{
    public const int MaxManifestCount = 2048;
    public const int MaxEntryCount = 4096;

    public static int GetManifestSize(BackendPacketManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var size = PacketWriter.GetStringSize(manifest.BackendKind) +
                   PacketWriter.GetStringSize(manifest.ManifestId.Value) +
                   PacketWriter.GetStringSize(manifest.Hash.Value) +
                   sizeof(int);
        foreach (var entry in manifest.Entries)
        {
            size += GetEntrySize(entry);
        }

        return size;
    }

    public static void WriteManifest(BackendPacketManifest manifest, ref PacketWriter writer)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        writer.WriteString(manifest.BackendKind);
        writer.WriteString(manifest.ManifestId.Value);
        writer.WriteString(manifest.Hash.Value);
        writer.WriteInt32(manifest.Entries.Length);
        foreach (var entry in manifest.Entries)
        {
            WriteEntry(entry, ref writer);
        }
    }

    public static BackendPacketManifest ReadManifest(ref PacketReader reader)
    {
        var backendKind = reader.ReadString();
        var manifestId = new BackendPacketManifestId(reader.ReadString());
        var expectedHash = new BackendPacketManifestHash(reader.ReadString());
        var entryCount = reader.ReadInt32();
        if (entryCount < 0 || entryCount > MaxEntryCount)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Backend packet manifest entry count.");
        }

        var entries = new BackendPacketManifestEntry[entryCount];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = ReadEntry(ref reader);
        }

        var manifest = new BackendPacketManifest(backendKind, manifestId, entries);
        if (manifest.Hash != expectedHash)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Backend packet manifest hash does not match the manifest content.");
        }

        return manifest;
    }

    public static int GetInfoSize(BackendPacketManifestInfo info)
    {
        return sizeof(long) +
               sizeof(byte) +
               GetManifestSize(info.Manifest) +
               PacketWriter.GetStringSize(info.AuditNote) +
               sizeof(long) +
               sizeof(long) +
               sizeof(byte) +
               (info.DeprecatedAt.HasValue ? sizeof(long) : 0);
    }

    public static void WriteInfo(BackendPacketManifestInfo info, ref PacketWriter writer)
    {
        writer.WriteInt64(info.Id);
        writer.WriteByte((byte)info.Lifecycle);
        WriteManifest(info.Manifest, ref writer);
        writer.WriteString(info.AuditNote);
        writer.WriteInt64(info.CreatedAt.Ticks);
        writer.WriteInt64(info.UpdatedAt.Ticks);
        writer.WriteByte(info.DeprecatedAt.HasValue ? (byte)1 : (byte)0);
        if (info.DeprecatedAt.HasValue)
        {
            writer.WriteInt64(info.DeprecatedAt.Value.Ticks);
        }
    }

    public static BackendPacketManifestInfo ReadInfo(ref PacketReader reader)
    {
        var id = reader.ReadInt64();
        var lifecycle = (BackendPacketManifestLifecycle)reader.ReadByte();
        var manifest = ReadManifest(ref reader);
        var auditNote = reader.ReadString();
        var createdAt = new DateTime(reader.ReadInt64());
        var updatedAt = new DateTime(reader.ReadInt64());
        var hasDeprecatedAt = reader.ReadByte() != 0;
        DateTime? deprecatedAt = hasDeprecatedAt
            ? new DateTime(reader.ReadInt64())
            : null;
        return new BackendPacketManifestInfo(
            id,
            manifest,
            lifecycle,
            auditNote,
            createdAt,
            updatedAt,
            deprecatedAt);
    }

    public static int GetEntrySize(BackendPacketManifestEntry entry)
    {
        return sizeof(byte) +
               sizeof(byte) +
               sizeof(ushort) +
               sizeof(ushort) +
               sizeof(byte) +
               sizeof(int) +
               sizeof(int) +
               sizeof(byte) +
               (entry.PayloadConstraint.FixedLength.HasValue ? sizeof(int) : 0) +
               PacketWriter.GetStringSize(entry.PayloadConstraint.SchemaId) +
               PacketWriter.GetStringSize(entry.PayloadConstraint.SchemaHash?.Value ?? string.Empty) +
               BackendPacketVerifierProgramCodec.GetNullableProgramSize(entry.PayloadConstraint.VerifierProgram);
    }

    public static void WriteEntry(BackendPacketManifestEntry entry, ref PacketWriter writer)
    {
        writer.WriteByte((byte)entry.Direction);
        writer.WriteByte((byte)entry.PacketKind);
        writer.WriteUInt16(entry.PacketId);
        writer.WriteUInt16(entry.RoutedVersion);
        writer.WriteByte((byte)entry.Status);
        writer.WriteInt32(entry.PayloadConstraint.MinimumLength);
        writer.WriteInt32(entry.PayloadConstraint.MaximumLength);
        writer.WriteByte(entry.PayloadConstraint.FixedLength.HasValue ? (byte)1 : (byte)0);
        if (entry.PayloadConstraint.FixedLength.HasValue)
        {
            writer.WriteInt32(entry.PayloadConstraint.FixedLength.Value);
        }

        writer.WriteString(entry.PayloadConstraint.SchemaId);
        writer.WriteString(entry.PayloadConstraint.SchemaHash?.Value ?? string.Empty);
        BackendPacketVerifierProgramCodec.WriteNullableProgram(entry.PayloadConstraint.VerifierProgram, ref writer);
    }

    public static BackendPacketManifestEntry ReadEntry(ref PacketReader reader)
    {
        var direction = (BackendPacketManifestDirection)reader.ReadByte();
        var packetKind = (PacketKind)reader.ReadByte();
        var packetId = reader.ReadUInt16();
        var routedVersion = reader.ReadUInt16();
        var status = (BackendPacketManifestEntryStatus)reader.ReadByte();
        var minimumLength = reader.ReadInt32();
        var maximumLength = reader.ReadInt32();
        var hasFixedLength = reader.ReadByte() != 0;
        int? fixedLength = hasFixedLength
            ? reader.ReadInt32()
            : null;
        var schemaId = reader.ReadString();
        var schemaHashValue = reader.ReadString();
        var schemaHash = string.IsNullOrWhiteSpace(schemaHashValue)
            ? (BackendPacketManifestHash?)null
            : new BackendPacketManifestHash(schemaHashValue);
        var verifierProgram = BackendPacketVerifierProgramCodec.ReadNullableProgram(ref reader);
        return new BackendPacketManifestEntry(
            direction,
            packetKind,
            packetId,
            routedVersion,
            new BackendPacketPayloadConstraint(
                minimumLength,
                maximumLength,
                fixedLength,
                schemaId,
                schemaHash,
                verifierProgram),
            status);
    }
}
