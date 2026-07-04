using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifest
{
    private static readonly byte[] s_HashHeader = Encoding.UTF8.GetBytes("BackendPacketManifest:v1");
    private readonly Dictionary<BackendPacketManifestEntryKey, BackendPacketManifestEntry> m_EntriesByKey;

    public BackendPacketManifest(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestEntry[] entries)
    {
        BackendKind = NormalizeBackendKind(backendKind);
        ManifestId = manifestId;
        Entries = SortEntries(entries ?? throw new ArgumentNullException(nameof(entries)));
        m_EntriesByKey = CreateEntryMap(Entries);
        Hash = CalculateHash(BackendKind, ManifestId, Entries);
    }

    public string BackendKind { get; }

    public BackendPacketManifestId ManifestId { get; }

    public BackendPacketManifestHash Hash { get; }

    public BackendPacketManifestEntry[] Entries { get; }

    public BackendPacketManifestValidationResult ValidatePacket(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        int payloadLength)
    {
        if (!BackendPacketManifestEntry.IsValidDirection(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (packetKind is not (PacketKind.Request or PacketKind.Response or PacketKind.Notify))
        {
            throw new ArgumentOutOfRangeException(nameof(packetKind));
        }

        if (packetId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetId));
        }

        if (routedVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedVersion));
        }

        var key = new BackendPacketManifestEntryKey(direction, packetKind, packetId, routedVersion);
        if (!m_EntriesByKey.TryGetValue(key, out var entry))
        {
            return BackendPacketManifestValidationResult.Rejected(GetMissingEntryFailure(direction, packetKind, packetId, routedVersion));
        }

        var payloadFailure = entry.PayloadConstraint.ValidatePayloadLength(payloadLength);
        return payloadFailure == BackendPacketManifestValidationFailure.None
            ? BackendPacketManifestValidationResult.Accepted(entry)
            : BackendPacketManifestValidationResult.Rejected(payloadFailure);
    }

    public BackendPacketManifestValidationResult ValidatePacket(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        ReadOnlySpan<byte> payload)
    {
        var result = ValidatePacket(
            direction,
            packetKind,
            packetId,
            routedVersion,
            payload.Length);
        if (!result.Success)
        {
            return result;
        }

        var entry = result.Entry ?? throw new InvalidOperationException("Accepted Backend packet manifest validation did not include an entry.");
        var payloadFailure = entry.PayloadConstraint.ValidatePayload(payload);
        return payloadFailure == BackendPacketManifestValidationFailure.None
            ? result
            : BackendPacketManifestValidationResult.Rejected(payloadFailure);
    }

    public bool MatchesAdvertisement(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash hash)
    {
        return string.Equals(BackendKind, NormalizeBackendKind(backendKind), StringComparison.Ordinal) &&
               ManifestId == manifestId &&
               Hash == hash;
    }

    private BackendPacketManifestValidationFailure GetMissingEntryFailure(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion)
    {
        var hasPacketId = false;
        foreach (var entry in Entries)
        {
            if (entry.Direction != direction ||
                entry.PacketId != packetId)
            {
                continue;
            }

            hasPacketId = true;
            if (entry.RoutedVersion == routedVersion &&
                entry.PacketKind != packetKind)
            {
                return BackendPacketManifestValidationFailure.PacketKindMismatch;
            }
        }

        return hasPacketId
            ? BackendPacketManifestValidationFailure.UnknownVersion
            : BackendPacketManifestValidationFailure.UnknownPacketId;
    }

    private static BackendPacketManifestHash CalculateHash(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestEntry[] entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(s_HashHeader.Length);
            writer.Write(s_HashHeader);
            WriteString(writer, backendKind);
            WriteString(writer, manifestId.Value);
            writer.Write(entries.Length);
            foreach (var entry in entries)
            {
                writer.Write((byte)entry.Direction);
                writer.Write((byte)entry.PacketKind);
                writer.Write(entry.PacketId);
                writer.Write(entry.RoutedVersion);
                writer.Write((byte)entry.Status);
                writer.Write(entry.PayloadConstraint.MinimumLength);
                writer.Write(entry.PayloadConstraint.MaximumLength);
                writer.Write(entry.PayloadConstraint.FixedLength.HasValue);
                if (entry.PayloadConstraint.FixedLength.HasValue)
                {
                    writer.Write(entry.PayloadConstraint.FixedLength.Value);
                }

                WriteString(writer, entry.PayloadConstraint.SchemaId);
                WriteString(writer, entry.PayloadConstraint.SchemaHash?.Value ?? string.Empty);
                WriteVerifierProgram(writer, entry.PayloadConstraint.VerifierProgram);
            }
        }

        using var sha256 = SHA256.Create();
        return BackendPacketManifestHash.FromBytes(sha256.ComputeHash(stream.ToArray()));
    }

    private static BackendPacketManifestEntry[] SortEntries(BackendPacketManifestEntry[] entries)
    {
        return entries
            .Select(static entry => entry ?? throw new ArgumentException("Backend packet manifest entries cannot contain null.", nameof(entries)))
            .OrderBy(static entry => entry.Direction)
            .ThenBy(static entry => entry.PacketKind)
            .ThenBy(static entry => entry.PacketId)
            .ThenBy(static entry => entry.RoutedVersion)
            .ToArray();
    }

    private static Dictionary<BackendPacketManifestEntryKey, BackendPacketManifestEntry> CreateEntryMap(
        BackendPacketManifestEntry[] entries)
    {
        var result = new Dictionary<BackendPacketManifestEntryKey, BackendPacketManifestEntry>();
        foreach (var entry in entries)
        {
            if (!result.TryAdd(entry.Key, entry))
            {
                throw new ArgumentException(
                    "Backend packet manifest cannot contain duplicate entries for the same direction, packet kind, packet id, and routed version.",
                    nameof(entries));
            }
        }

        return result;
    }

    public static string NormalizeBackendKind(string backendKind)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        var normalized = backendKind.Trim();
        if (normalized.Length > BackendPacketManifestId.MaxLength)
        {
            throw new ArgumentException($"Backend kind must be {BackendPacketManifestId.MaxLength} characters or fewer.", nameof(backendKind));
        }

        return normalized;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteVerifierProgram(
        BinaryWriter writer,
        BackendPacketVerifierProgram? program)
    {
        var encoded = program == null
            ? Array.Empty<byte>()
            : BackendPacketVerifierProgramCodec.Encode(program);
        writer.Write(encoded.Length);
        writer.Write(encoded);
    }
}
