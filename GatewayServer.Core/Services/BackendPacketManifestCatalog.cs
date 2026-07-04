using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IBackendPacketManifestProvider
{
    BackendPacketManifest RequireManifest(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash);

    BackendPacketManifestValidationResult ValidatePacket(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        BackendPacketManifestDirection direction,
        PacketCore.PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        ReadOnlySpan<byte> payload);

    ServiceAdminStatusItem[] GetStatusItems();
}

internal interface IBackendPacketManifestWriter
{
    void Publish(BackendPacketManifestSnapshot snapshot);
}

internal sealed class BackendPacketManifestCatalog :
    IBackendPacketManifestProvider,
    IBackendPacketManifestWriter
{
    private readonly object m_Sync = new();
    private Dictionary<BackendPacketManifestKey, BackendPacketManifest> m_Manifests = new();
    private DateTimeOffset? m_LastUpdatedAt;

    public BackendPacketManifest RequireManifest(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash)
    {
        var key = new BackendPacketManifestKey(
            BackendPacketManifest.NormalizeBackendKind(backendKind),
            manifestId,
            manifestHash);
        lock (m_Sync)
        {
            if (m_Manifests.TryGetValue(key, out var manifest))
            {
                return manifest;
            }
        }

        throw new InvalidOperationException(
            $"Backend packet manifest is not loaded. BackendKind={key.BackendKind}, ManifestId={manifestId.Value}, ManifestHash={manifestHash.Value}.");
    }

    public BackendPacketManifestValidationResult ValidatePacket(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        BackendPacketManifestDirection direction,
        PacketCore.PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        ReadOnlySpan<byte> payload)
    {
        return RequireManifest(backendKind, manifestId, manifestHash)
            .ValidatePacket(
                direction,
                packetKind,
                packetId,
                routedVersion,
                payload);
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        BackendPacketManifest[] manifests;
        DateTimeOffset? lastUpdatedAt;
        lock (m_Sync)
        {
            manifests = [.. m_Manifests.Values];
            lastUpdatedAt = m_LastUpdatedAt;
        }

        var items = new List<ServiceAdminStatusItem>
        {
            new("Backend packet manifests", "Loaded manifests", manifests.Length.ToString()),
            new("Backend packet manifests", "Last update", lastUpdatedAt.HasValue
                ? lastUpdatedAt.Value.LocalDateTime.ToString("O")
                : "Never")
        };

        foreach (var group in manifests
                     .GroupBy(static manifest => manifest.BackendKind, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new ServiceAdminStatusItem(
                $"Backend packet manifest {group.Key}",
                "Loaded manifests",
                group.Count().ToString()));
        }

        return [.. items];
    }

    public void Publish(BackendPacketManifestSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var manifests = new Dictionary<BackendPacketManifestKey, BackendPacketManifest>();
        foreach (var manifest in snapshot.Manifests)
        {
            foreach (var entry in manifest.Entries)
            {
                entry.PayloadConstraint.VerifierProgram?.Validate();
            }

            var key = new BackendPacketManifestKey(
                manifest.BackendKind,
                manifest.ManifestId,
                manifest.Hash);
            manifests[key] = manifest;
        }

        lock (m_Sync)
        {
            m_Manifests = manifests;
            m_LastUpdatedAt = snapshot.ObservedAt;
        }
    }

    private readonly struct BackendPacketManifestKey : IEquatable<BackendPacketManifestKey>
    {
        public BackendPacketManifestKey(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash)
        {
            BackendKind = BackendPacketManifest.NormalizeBackendKind(backendKind);
            ManifestId = manifestId;
            ManifestHash = manifestHash;
        }

        public string BackendKind { get; }

        public BackendPacketManifestId ManifestId { get; }

        public BackendPacketManifestHash ManifestHash { get; }

        public bool Equals(BackendPacketManifestKey other)
        {
            return string.Equals(BackendKind, other.BackendKind, StringComparison.Ordinal) &&
                   ManifestId == other.ManifestId &&
                   ManifestHash == other.ManifestHash;
        }

        public override bool Equals(object? obj)
        {
            return obj is BackendPacketManifestKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(BackendKind),
                ManifestId,
                ManifestHash);
        }
    }
}
