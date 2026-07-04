using System;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed class BackendPacketManifestInput
{
    public const int MaxAuditNoteLength = 512;

    public BackendPacketManifestInput(
        BackendPacketManifest manifest,
        string auditNote)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        AuditNote = NormalizeAuditNote(auditNote);
    }

    public BackendPacketManifest Manifest { get; }

    public string AuditNote { get; }

    public static string NormalizeAuditNote(string? auditNote)
    {
        var normalized = string.IsNullOrWhiteSpace(auditNote)
            ? string.Empty
            : auditNote.Trim();
        if (normalized.Length > MaxAuditNoteLength)
        {
            throw new ArgumentException($"Audit note must be {MaxAuditNoteLength} characters or fewer.", nameof(auditNote));
        }

        return normalized;
    }
}
