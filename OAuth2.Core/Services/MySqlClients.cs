using System.Data;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlClients(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IClients
{
    public async ValueTask<string> AddClientAsync(string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        string id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        const string QUERY1 = "INSERT INTO `client` (`id`, `owner_id`, `name`) VALUES(@id, @ownerId, @name)";
        var command = new CommandDefinition(QUERY1, new { id, ownerId, name }, tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string QUERY2 = "INSERT INTO `client_claim` (`client_id`, `name`, `value`) VALUES(@id, 'redirect_uri', @value)";
        command = new CommandDefinition(QUERY2, redirectUris.Select(value => new { id, value }), tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string QUERY3 = "INSERT INTO `client_claim` (`client_id`, `name`, `value`) VALUES(@id, 'scope', @value)";
        command = new CommandDefinition(QUERY3, ScopePolicy.SupportedScopes.Select(value => new { id, value }), tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        await tx.CommitAsync(cancellationToken);
        return id;
    }

    public async ValueTask<ClientInfo?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `id`, `owner_id` AS `OwnerId`, `name`, `created_at` AS `CreatedAt` FROM `client` WHERE `id` = @clientId AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { clientId }, cancellationToken: cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<ClientInfo>(command);
        if (result == default)
        {
            return null;
        }

        const string QUERY2 = "SELECT `value` FROM `client_claim` WHERE `client_id` = @clientId AND `removed_at` IS NULL AND `name` = 'redirect_uri'";
        command = new CommandDefinition(QUERY2, new { clientId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<string>(command);

        return result with { RedirectUris = [.. results] };
    }

    public async ValueTask<ClientInfo[]> GetClientsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `id`, `name`, `created_at` AS `CreatedAt` FROM `client` WHERE `owner_id` = @ownerId AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { ownerId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ClientInfo>(command);

        return [.. results.Select(p => p with { OwnerId = ownerId })];
    }

    public async ValueTask<string> NewClientSecretAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        const string QUERY1 = "INSERT INTO `client_claim` (`client_id`, `name`, `value`) VALUES(@clientId, 'secret', @value)";
        var command = new CommandDefinition(QUERY1, new { clientId, value = PasswordHasher.Hash(secret) }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        return secret;
    }

    public async ValueTask<ClientSecretInfo[]> GetClientSecretsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "SELECT `id`, `client_id` AS `ClientId`, `created_at` AS `CreatedAt` FROM `client_claim` WHERE `client_id` = @clientId AND `removed_at` IS NULL AND `name` = 'secret' ORDER BY `created_at` ASC";
        var command = new CommandDefinition(QUERY, new { clientId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ClientSecretInfo>(command);

        return [.. results];
    }

    public async ValueTask RemoveClientSecretAsync(long secretId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "UPDATE `client_claim` SET `removed_at` = NOW() WHERE `id` = @secretId AND `name` = 'secret' AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { secretId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        const string QUERY_CLIENT = "UPDATE `client` SET `removed_at` = NOW() WHERE `id` = @clientId AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY_CLIENT, new { clientId }, tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string QUERY_CLAIMS = "UPDATE `client_claim` SET `removed_at` = NOW() WHERE `client_id` = @clientId AND `removed_at` IS NULL";
        command = new CommandDefinition(QUERY_CLAIMS, new { clientId }, tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string QUERY_GROUPS = "UPDATE `client_user_group` SET `removed_at` = NOW() WHERE `client_id` = @clientId AND `removed_at` IS NULL";
        command = new CommandDefinition(QUERY_GROUPS, new { clientId }, tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        await tx.CommitAsync(cancellationToken);
    }
}
