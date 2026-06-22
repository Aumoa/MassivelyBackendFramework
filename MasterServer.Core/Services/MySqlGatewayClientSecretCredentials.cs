using System.Security.Cryptography;
using System.Text;
using Dapper;
using MasterServer.Options;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace MasterServer.Services;

internal sealed class MySqlGatewayClientSecretCredentials(
    IOptions<ServiceConnectionCredentialOptions> options) : IGatewayClientSecretCredentials
{
    private const string AccessTokenPrefix = "gwc_";

    public async ValueTask<GatewayClientSecretCredentialInfo[]> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = GetConnection();
        const string QUERY = """
SELECT
    `id` AS `Id`,
    `token_id` AS `TokenId`,
    `subject_id` AS `SubjectId`,
    `display_name` AS `DisplayName`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `gateway_client_secret_credential`
WHERE `removed_at` IS NULL
ORDER BY `display_name`, `subject_id`, `id`;
""";
        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<GatewayClientSecretCredentialRow>(command).ConfigureAwait(false);
        return [.. results.Select(static row => row.ToInfo())];
    }

    public async ValueTask<GatewayClientSecretValidationInfo[]> GetActiveSecretsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = GetConnection();
        const string QUERY = """
SELECT
    `token_id` AS `TokenId`,
    `subject_id` AS `SubjectId`,
    `secret_hash` AS `SecretHash`
FROM `gateway_client_secret_credential`
WHERE `enabled` = 1
  AND `removed_at` IS NULL
ORDER BY `token_id`;
""";
        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<GatewayClientSecretValidationRow>(command).ConfigureAwait(false);
        return [.. results.Select(static row => row.ToInfo())];
    }

    public async ValueTask<GatewayClientSecretCredentialCreated> CreateCredentialAsync(
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        var issuedSecret = CreateIssuedSecret();
        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string INSERT_QUERY = """
INSERT INTO `gateway_client_secret_credential`
    (`token_id`, `subject_id`, `display_name`, `secret_hash`, `enabled`)
VALUES
    (@tokenId, @subjectId, @displayName, @secretHash, @enabled);
""";
        var command = new CommandDefinition(
            INSERT_QUERY,
            new
            {
                tokenId = issuedSecret.TokenId,
                subjectId = input.SubjectId.Trim(),
                displayName = input.DisplayName.Trim(),
                secretHash = HashSecret(issuedSecret.Secret),
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);

        var credential = await SelectInsertedCredentialAsync(connection, cancellationToken).ConfigureAwait(false);
        return new GatewayClientSecretCredentialCreated(credential, issuedSecret.AccessToken);
    }

    public async ValueTask UpdateCredentialAsync(
        long id,
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ValidateInput(input);

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `gateway_client_secret_credential`
SET
    `subject_id` = @subjectId,
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
                subjectId = input.SubjectId.Trim(),
                displayName = input.DisplayName.Trim(),
                input.Enabled
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    public async ValueTask<string> RotateSecretAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var issuedSecret = CreateIssuedSecret();
        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `gateway_client_secret_credential`
SET
    `token_id` = @tokenId,
    `secret_hash` = @secretHash,
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(
            QUERY,
            new
            {
                id,
                tokenId = issuedSecret.TokenId,
                secretHash = HashSecret(issuedSecret.Secret)
            },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
        return issuedSecret.AccessToken;
    }

    public async ValueTask RemoveCredentialAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await using var connection = GetConnection();
        const string QUERY = """
UPDATE `gateway_client_secret_credential`
SET
    `removed_at` = NOW(),
    `updated_at` = NOW()
WHERE `id` = @id
  AND `removed_at` IS NULL;
""";
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    private static async ValueTask<GatewayClientSecretCredentialInfo> SelectInsertedCredentialAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string SELECT_QUERY = """
SELECT
    `id` AS `Id`,
    `token_id` AS `TokenId`,
    `subject_id` AS `SubjectId`,
    `display_name` AS `DisplayName`,
    `enabled` AS `Enabled`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `gateway_client_secret_credential`
WHERE `id` = LAST_INSERT_ID();
""";
        var command = new CommandDefinition(SELECT_QUERY, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<GatewayClientSecretCredentialRow>(command).ConfigureAwait(false);
        return row.ToInfo();
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static void ValidateInput(GatewayClientSecretCredentialInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.SubjectId))
        {
            throw new ArgumentException("Gateway client subject id is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new ArgumentException("Gateway client display name is required.", nameof(input));
        }

        if (input.SubjectId.Trim().Length > 128)
        {
            throw new ArgumentException("Gateway client subject id must be 128 characters or fewer.", nameof(input));
        }

        if (input.DisplayName.Trim().Length > 256)
        {
            throw new ArgumentException("Gateway client display name must be 256 characters or fewer.", nameof(input));
        }
    }

    private static IssuedSecret CreateIssuedSecret()
    {
        var tokenId = ToBase64Url(RandomNumberGenerator.GetBytes(16));
        var secret = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        return new IssuedSecret(tokenId, secret, $"{AccessTokenPrefix}{tokenId}.{secret}");
    }

    private static string HashSecret(string secret)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record IssuedSecret(
        string TokenId,
        string Secret,
        string AccessToken);

    private sealed record GatewayClientSecretCredentialRow(
        long Id,
        string TokenId,
        string SubjectId,
        string DisplayName,
        bool Enabled,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public GatewayClientSecretCredentialInfo ToInfo()
        {
            return new GatewayClientSecretCredentialInfo(
                Id,
                TokenId,
                SubjectId,
                DisplayName,
                Enabled,
                CreatedAt,
                UpdatedAt);
        }
    }

    private sealed record GatewayClientSecretValidationRow(
        string TokenId,
        string SubjectId,
        string SecretHash)
    {
        public GatewayClientSecretValidationInfo ToInfo()
        {
            return new GatewayClientSecretValidationInfo(TokenId, SubjectId, SecretHash);
        }
    }
}
