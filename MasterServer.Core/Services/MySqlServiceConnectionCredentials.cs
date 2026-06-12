using System.Security.Cryptography;
using Dapper;
using MasterServer.ControlPlane;
using MasterServer.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace MasterServer.Services;

internal sealed class MySqlServiceConnectionCredentials(
    IOptions<ServiceConnectionCredentialOptions> options,
    IDataProtectionProvider dataProtectionProvider) : IServiceConnectionCredentials
{
    private const string ProtectorPurpose = "MasterServer.ServiceConnectionCredentials.v1";

    private readonly IDataProtector m_SecretProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async ValueTask<ServiceConnectionCredentialInfo[]> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = GetConnection();
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `node_kind` AS `NodeKind`,
    `node_id` AS `NodeId`,
    `display_name` AS `DisplayName`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `service_connection_credential`
WHERE `removed_at` IS NULL
ORDER BY `node_kind`, `node_id`;
""";
        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ServiceConnectionCredentialRow>(command);
        return [.. results.Select(static row => row.ToInfo())];
    }

    public async ValueTask<ServiceConnectionCredentialCreated> CreateCredentialAsync(
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        var sharedSecret = CreateSharedSecret();
        var protectedSecret = m_SecretProtector.Protect(sharedSecret);

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string INSERT_QUERY = """
INSERT INTO `service_connection_credential`
    (`node_kind`, `node_id`, `display_name`, `protected_secret`, `enabled`)
VALUES
    (@nodeKind, @nodeId, @displayName, @protectedSecret, @enabled);
""";
        var command = new CommandDefinition(
            INSERT_QUERY,
            new
            {
                nodeKind = (byte)input.NodeKind,
                input.NodeId,
                input.DisplayName,
                protectedSecret,
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string SELECT_QUERY = """
SELECT
    `id` AS `Id`,
    `node_kind` AS `NodeKind`,
    `node_id` AS `NodeId`,
    `display_name` AS `DisplayName`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `service_connection_credential`
WHERE `id` = LAST_INSERT_ID();
""";
        command = new CommandDefinition(SELECT_QUERY, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<ServiceConnectionCredentialRow>(command);
        return new ServiceConnectionCredentialCreated(row.ToInfo(), sharedSecret);
    }

    public async ValueTask UpdateCredentialAsync(
        long id,
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ValidateInput(input);

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `service_connection_credential`
SET
    `node_kind` = @nodeKind,
    `node_id` = @nodeId,
    `display_name` = @displayName,
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
                nodeKind = (byte)input.NodeKind,
                input.NodeId,
                input.DisplayName,
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask<string> RotateSecretAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var sharedSecret = CreateSharedSecret();
        var protectedSecret = m_SecretProtector.Protect(sharedSecret);

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `service_connection_credential`
SET
    `protected_secret` = @protectedSecret,
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(
            QUERY,
            new { id, protectedSecret },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
        return sharedSecret;
    }

    public async ValueTask RemoveCredentialAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `service_connection_credential`
SET
    `removed_at` = NOW(),
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static string CreateSharedSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static void ValidateInput(ServiceConnectionCredentialInput input)
    {
        if (!BackendNodeEndpoint.IsBackendNodeKind(input.NodeKind) &&
            input.NodeKind is not MasterNodeKind.Gateway and not MasterNodeKind.Dedicated and not MasterNodeKind.MasterAdmin)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Unsupported node kind.");
        }

        if (string.IsNullOrWhiteSpace(input.NodeId))
        {
            throw new ArgumentException("Node id is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new ArgumentException("Display name is required.", nameof(input));
        }
    }

    private sealed record ServiceConnectionCredentialRow(
        long Id,
        byte NodeKind,
        string NodeId,
        string DisplayName,
        bool Enabled,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public ServiceConnectionCredentialInfo ToInfo()
        {
            return new ServiceConnectionCredentialInfo(
                Id,
                (MasterNodeKind)NodeKind,
                NodeId,
                DisplayName,
                Enabled,
                CreatedAt,
                UpdatedAt);
        }
    }
}
