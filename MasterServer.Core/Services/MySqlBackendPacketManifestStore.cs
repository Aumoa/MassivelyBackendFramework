using Dapper;
using MasterServer.ControlPlane;
using MasterServer.Options;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using PacketCore;

namespace MasterServer.Services;

internal sealed class MySqlBackendPacketManifestStore(
    IOptions<ServiceConnectionCredentialOptions> options) : IBackendPacketManifestStore
{
    private const int MaxManifestEntries = 4096;

    public async ValueTask<BackendPacketManifest[]> GetGatewayManifestsAsync(CancellationToken cancellationToken = default)
    {
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `manifest_id` AS `ManifestId`,
    `manifest_hash` AS `ManifestHash`,
    `lifecycle` AS `Lifecycle`,
    `audit_note` AS `AuditNote`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`,
    `deprecated_at` AS `DeprecatedAt`
FROM `backend_packet_manifest`
WHERE `removed_at` IS NULL
  AND `lifecycle` IN (1, 2)
ORDER BY `backend_kind`, `manifest_id`, `manifest_hash`;
""";
        var manifests = await QueryInfosAsync(QUERY, null, cancellationToken).ConfigureAwait(false);
        return [.. manifests.Select(static info => info.Manifest)];
    }

    public async ValueTask<BackendPacketManifestInfo[]> GetManifestInfosAsync(CancellationToken cancellationToken = default)
    {
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `manifest_id` AS `ManifestId`,
    `manifest_hash` AS `ManifestHash`,
    `lifecycle` AS `Lifecycle`,
    `audit_note` AS `AuditNote`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`,
    `deprecated_at` AS `DeprecatedAt`
FROM `backend_packet_manifest`
WHERE `removed_at` IS NULL
ORDER BY `backend_kind`, `manifest_id`, `manifest_hash`;
""";
        return await QueryInfosAsync(QUERY, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BackendPacketManifestInfo?> FindApprovedManifestAsync(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash hash,
        CancellationToken cancellationToken = default)
    {
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `manifest_id` AS `ManifestId`,
    `manifest_hash` AS `ManifestHash`,
    `lifecycle` AS `Lifecycle`,
    `audit_note` AS `AuditNote`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`,
    `deprecated_at` AS `DeprecatedAt`
FROM `backend_packet_manifest`
WHERE `removed_at` IS NULL
  AND `lifecycle` IN (1, 2)
  AND `backend_kind` = @backendKind
  AND `manifest_id` = @manifestId
  AND `manifest_hash` = @manifestHash
LIMIT 1;
""";
        var normalizedBackendKind = BackendPacketManifest.NormalizeBackendKind(backendKind);
        var manifests = await QueryInfosAsync(
            QUERY,
            new
            {
                backendKind = normalizedBackendKind,
                manifestId = manifestId.Value,
                manifestHash = hash.Value
            },
            cancellationToken).ConfigureAwait(false);
        return manifests.SingleOrDefault();
    }

    public async ValueTask<BackendPacketManifestInfo> CreateManifestAsync(
        BackendPacketManifestInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var manifestId = await InsertManifestAsync(connection, transaction, input, cancellationToken).ConfigureAwait(false);
        await InsertEntriesAsync(connection, transaction, manifestId, input.Manifest.Entries, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var created = await GetManifestInfoByIdAsync(manifestId, cancellationToken).ConfigureAwait(false);
        return created ?? throw new InvalidOperationException("Created Backend packet manifest could not be loaded.");
    }

    public async ValueTask UpdateManifestAsync(
        long id,
        BackendPacketManifestInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ValidateInput(input);

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        const string UPDATE_QUERY = """
UPDATE `backend_packet_manifest`
SET
    `backend_kind` = @backendKind,
    `manifest_id` = @manifestId,
    `manifest_hash` = @manifestHash,
    `lifecycle` = 1,
    `audit_note` = @auditNote,
    `updated_at` = NOW(),
    `deprecated_at` = NULL
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            UPDATE_QUERY,
            new
            {
                id,
                backendKind = input.Manifest.BackendKind,
                manifestId = input.Manifest.ManifestId.Value,
                manifestHash = input.Manifest.Hash.Value,
                input.AuditNote
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated == 0)
        {
            throw new InvalidOperationException("Backend packet manifest was not found.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM `backend_packet_manifest_entry` WHERE `manifest_record_id` = @id;",
            new { id },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await InsertEntriesAsync(connection, transaction, id, input.Manifest.Entries, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeprecateManifestAsync(
        long id,
        string auditNote,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var normalizedAuditNote = BackendPacketManifestInput.NormalizeAuditNote(auditNote);
        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `backend_packet_manifest`
SET
    `lifecycle` = 2,
    `audit_note` = @auditNote,
    `updated_at` = NOW(),
    `deprecated_at` = COALESCE(`deprecated_at`, NOW())
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            QUERY,
            new
            {
                id,
                auditNote = normalizedAuditNote
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated == 0)
        {
            throw new InvalidOperationException("Backend packet manifest was not found.");
        }
    }

    public async ValueTask RemoveManifestAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `backend_packet_manifest`
SET
    `removed_at` = NOW(),
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            QUERY,
            new { id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated == 0)
        {
            throw new InvalidOperationException("Backend packet manifest was not found.");
        }
    }

    private async ValueTask<BackendPacketManifestInfo?> GetManifestInfoByIdAsync(
        long id,
        CancellationToken cancellationToken)
    {
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `manifest_id` AS `ManifestId`,
    `manifest_hash` AS `ManifestHash`,
    `lifecycle` AS `Lifecycle`,
    `audit_note` AS `AuditNote`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`,
    `deprecated_at` AS `DeprecatedAt`
FROM `backend_packet_manifest`
WHERE `id` = @id
  AND `removed_at` IS NULL
LIMIT 1;
""";
        var manifests = await QueryInfosAsync(QUERY, new { id }, cancellationToken).ConfigureAwait(false);
        return manifests.SingleOrDefault();
    }

    private async Task<BackendPacketManifestInfo[]> QueryInfosAsync(
        string query,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = GetConnection();
        var manifestRows = (await connection.QueryAsync<ManifestRow>(new CommandDefinition(
            query,
            parameters,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
        if (manifestRows.Length == 0)
        {
            return [];
        }

        var manifestRecordIds = manifestRows.Select(static row => row.Id).ToArray();
        const string ENTRY_QUERY = """
SELECT
    `manifest_record_id` AS `ManifestRecordId`,
    `direction` AS `Direction`,
    `packet_kind` AS `PacketKind`,
    `packet_id` AS `PacketId`,
    `routed_version` AS `RoutedVersion`,
    `minimum_payload_length` AS `MinimumPayloadLength`,
    `maximum_payload_length` AS `MaximumPayloadLength`,
    `fixed_payload_length` AS `FixedPayloadLength`,
    `schema_id` AS `SchemaId`,
    `schema_hash` AS `SchemaHash`,
    `entry_status` AS `EntryStatus`
FROM `backend_packet_manifest_entry`
WHERE `manifest_record_id` IN @manifestRecordIds
ORDER BY
    `manifest_record_id`,
    `direction`,
    `packet_kind`,
    `packet_id`,
    `routed_version`;
""";
        var entryRows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            ENTRY_QUERY,
            new { manifestRecordIds },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        var entriesByManifestId = entryRows
            .GroupBy(static row => row.ManifestRecordId)
            .ToDictionary(static group => group.Key, static group => group.Select(static row => row.ToEntry()).ToArray());

        return [.. manifestRows.Select(row =>
        {
            entriesByManifestId.TryGetValue(row.Id, out var entries);
            return row.ToInfo(entries ?? []);
        })];
    }

    private static async ValueTask<long> InsertManifestAsync(
        MySqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        BackendPacketManifestInput input,
        CancellationToken cancellationToken)
    {
        const string INSERT_QUERY = """
INSERT INTO `backend_packet_manifest`
    (`backend_kind`, `manifest_id`, `manifest_hash`, `lifecycle`, `audit_note`)
VALUES
    (@backendKind, @manifestId, @manifestHash, 1, @auditNote);
SELECT LAST_INSERT_ID();
""";
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            INSERT_QUERY,
            new
            {
                backendKind = input.Manifest.BackendKind,
                manifestId = input.Manifest.ManifestId.Value,
                manifestHash = input.Manifest.Hash.Value,
                input.AuditNote
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async ValueTask InsertEntriesAsync(
        MySqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        long manifestRecordId,
        BackendPacketManifestEntry[] entries,
        CancellationToken cancellationToken)
    {
        const string INSERT_QUERY = """
INSERT INTO `backend_packet_manifest_entry`
    (`manifest_record_id`,
     `direction`,
     `packet_kind`,
     `packet_id`,
     `routed_version`,
     `minimum_payload_length`,
     `maximum_payload_length`,
     `fixed_payload_length`,
     `schema_id`,
     `schema_hash`,
     `entry_status`)
VALUES
    (@manifestRecordId,
     @direction,
     @packetKind,
     @packetId,
     @routedVersion,
     @minimumPayloadLength,
     @maximumPayloadLength,
     @fixedPayloadLength,
     @schemaId,
     @schemaHash,
     @entryStatus);
""";
        foreach (var entry in entries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                INSERT_QUERY,
                new
                {
                    manifestRecordId,
                    direction = (byte)entry.Direction,
                    packetKind = (byte)entry.PacketKind,
                    packetId = entry.PacketId,
                    routedVersion = entry.RoutedVersion,
                    minimumPayloadLength = entry.PayloadConstraint.MinimumLength,
                    maximumPayloadLength = entry.PayloadConstraint.MaximumLength,
                    fixedPayloadLength = entry.PayloadConstraint.FixedLength,
                    schemaId = string.IsNullOrWhiteSpace(entry.PayloadConstraint.SchemaId)
                        ? null
                        : entry.PayloadConstraint.SchemaId,
                    schemaHash = entry.PayloadConstraint.SchemaHash?.Value,
                    entryStatus = (byte)entry.Status
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static void ValidateInput(BackendPacketManifestInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.Manifest.Entries.Length > MaxManifestEntries)
        {
            throw new ArgumentException($"Backend packet manifest can contain at most {MaxManifestEntries} entries.", nameof(input));
        }
    }

    private sealed record ManifestRow(
        long Id,
        string BackendKind,
        string ManifestId,
        string ManifestHash,
        byte Lifecycle,
        string AuditNote,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeprecatedAt)
    {
        public BackendPacketManifestInfo ToInfo(BackendPacketManifestEntry[] entries)
        {
            var manifest = new BackendPacketManifest(
                BackendKind,
                new BackendPacketManifestId(ManifestId),
                entries);
            var storedHash = new BackendPacketManifestHash(ManifestHash);
            if (manifest.Hash != storedHash)
            {
                throw new InvalidOperationException(
                    $"Stored Backend packet manifest hash does not match content. BackendKind={BackendKind}, ManifestId={ManifestId}.");
            }

            return new BackendPacketManifestInfo(
                Id,
                manifest,
                (BackendPacketManifestLifecycle)Lifecycle,
                AuditNote ?? string.Empty,
                CreatedAt,
                UpdatedAt,
                DeprecatedAt);
        }
    }

    private sealed record EntryRow(
        long ManifestRecordId,
        byte Direction,
        byte PacketKind,
        int PacketId,
        int RoutedVersion,
        int MinimumPayloadLength,
        int MaximumPayloadLength,
        int? FixedPayloadLength,
        string? SchemaId,
        string? SchemaHash,
        byte EntryStatus)
    {
        public BackendPacketManifestEntry ToEntry()
        {
            if (PacketId is <= 0 or > ushort.MaxValue)
            {
                throw new InvalidOperationException("Stored Backend packet manifest packet id is out of range.");
            }

            if (RoutedVersion is <= 0 or > ushort.MaxValue)
            {
                throw new InvalidOperationException("Stored Backend packet manifest routed version is out of range.");
            }

            var schemaHash = string.IsNullOrWhiteSpace(SchemaHash)
                ? (BackendPacketManifestHash?)null
                : new BackendPacketManifestHash(SchemaHash);
            return new BackendPacketManifestEntry(
                (BackendPacketManifestDirection)Direction,
                (PacketKind)PacketKind,
                (ushort)PacketId,
                (ushort)RoutedVersion,
                new BackendPacketPayloadConstraint(
                    MinimumPayloadLength,
                    MaximumPayloadLength,
                    FixedPayloadLength,
                    SchemaId,
                    schemaHash),
                (BackendPacketManifestEntryStatus)EntryStatus);
        }
    }
}
