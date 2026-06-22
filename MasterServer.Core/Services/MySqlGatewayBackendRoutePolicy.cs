using Dapper;
using MasterServer.Options;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace MasterServer.Services;

internal sealed class MySqlGatewayBackendRoutePolicy(
    IOptions<ServiceConnectionCredentialOptions> options) : IGatewayBackendRoutePolicy
{
    public async ValueTask<string[]> GetAllowedBackendKindsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = GetConnection();
        const string QUERY = """
SELECT `backend_kind`
FROM `gateway_backend_route_policy_entry`
WHERE `enabled` = 1
  AND `removed_at` IS NULL
ORDER BY `backend_kind`;
""";
        var command = new CommandDefinition(cancellationToken: cancellationToken, commandText: QUERY);
        var results = await connection.QueryAsync<string>(command).ConfigureAwait(false);
        return [.. results];
    }

    public async ValueTask<GatewayBackendRoutePolicyEntryInfo[]> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = GetConnection();
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `gateway_backend_route_policy_entry`
WHERE `removed_at` IS NULL
ORDER BY `backend_kind`;
""";
        var command = new CommandDefinition(cancellationToken: cancellationToken, commandText: QUERY);
        var results = await connection.QueryAsync<GatewayBackendRoutePolicyEntryRow>(command).ConfigureAwait(false);
        return [.. results.Select(static row => row.ToInfo())];
    }

    public async ValueTask<GatewayBackendRoutePolicyEntryInfo> CreateEntryAsync(
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string INSERT_QUERY = """
INSERT INTO `gateway_backend_route_policy_entry`
    (`backend_kind`, `enabled`)
VALUES
    (@backendKind, @enabled);
""";
        var command = new CommandDefinition(
            INSERT_QUERY,
            new
            {
                backendKind = input.BackendKind,
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);

        const string SELECT_QUERY = """
SELECT
    `id` AS `Id`,
    `backend_kind` AS `BackendKind`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `gateway_backend_route_policy_entry`
WHERE `id` = LAST_INSERT_ID();
""";
        command = new CommandDefinition(SELECT_QUERY, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<GatewayBackendRoutePolicyEntryRow>(command).ConfigureAwait(false);
        return row.ToInfo();
    }

    public async ValueTask UpdateEntryAsync(
        long id,
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ValidateInput(input);

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `gateway_backend_route_policy_entry`
SET
    `backend_kind` = @backendKind,
    `enabled` = @enabled,
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(
            QUERY,
            new
            {
                id,
                backendKind = input.BackendKind,
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    public async ValueTask RemoveEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `gateway_backend_route_policy_entry`
SET
    `removed_at` = NOW(),
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static void ValidateInput(GatewayBackendRoutePolicyEntryInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.BackendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(input));
        }

        if (input.BackendKind.Length > 128)
        {
            throw new ArgumentException("Backend kind must be 128 characters or fewer.", nameof(input));
        }
    }

    private sealed record GatewayBackendRoutePolicyEntryRow(
        long Id,
        string BackendKind,
        bool Enabled,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public GatewayBackendRoutePolicyEntryInfo ToInfo()
        {
            return new GatewayBackendRoutePolicyEntryInfo(
                Id,
                BackendKind,
                Enabled,
                CreatedAt,
                UpdatedAt);
        }
    }
}
