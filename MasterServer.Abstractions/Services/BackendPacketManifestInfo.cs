using System;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed class BackendPacketManifestInfo
{
    public BackendPacketManifestInfo(
        long id,
        BackendPacketManifest manifest,
        BackendPacketManifestLifecycle lifecycle,
        string auditNote,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? deprecatedAt)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (!Enum.IsDefined(typeof(BackendPacketManifestLifecycle), lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        }

        Id = id;
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Lifecycle = lifecycle;
        AuditNote = auditNote ?? throw new ArgumentNullException(nameof(auditNote));
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DeprecatedAt = deprecatedAt;
    }

    public long Id { get; }

    public BackendPacketManifest Manifest { get; }

    public BackendPacketManifestLifecycle Lifecycle { get; }

    public string AuditNote { get; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public DateTime? DeprecatedAt { get; }
}
