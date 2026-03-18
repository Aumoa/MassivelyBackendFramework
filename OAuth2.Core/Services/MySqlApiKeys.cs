using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlApiKeys(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IApiKeys
{
    private const string ApiKeyPrefix = "mbf_";
    private const int KeyPrefixLength = 8;

    public async ValueTask<string> CreateApiKeyAsync(string accountId, string? allowedClientId, string? allowedScope, string name, CancellationToken cancellationToken = default)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var keyBody = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var apiKey = $"{ApiKeyPrefix}{keyBody}";
        var keyPrefix = keyBody[..KeyPrefixLength];
        var keyHash = PasswordHasher.Hash(apiKey);

        using var connection = GetConnection();
        const string QUERY = "INSERT INTO `client_api_key` (`account_id`, `name`, `key_prefix`, `key_hash`, `allowed_client_id`, `allowed_scope`) VALUES (@accountId, @name, @keyPrefix, @keyHash, @allowedClientId, @allowedScope)";
        var command = new CommandDefinition(QUERY, new { accountId, name, keyPrefix, keyHash, allowedClientId, allowedScope }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        return apiKey;
    }

    public async ValueTask<ApiKeyInfo[]> GetApiKeysAsync(string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        const string QUERY = "SELECT `id`, `account_id` AS `AccountId`, `allowed_client_id` AS `AllowedClientId`, `allowed_scope` AS `AllowedScope`, `name`, `created_at` AS `CreatedAt` FROM `client_api_key` WHERE `account_id` = @accountId AND `removed_at` IS NULL ORDER BY `created_at` ASC";
        var command = new CommandDefinition(QUERY, new { accountId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ApiKeyInfo>(command);
        return [.. results];
    }

    public async ValueTask<ApiKeyInfo?> GetApiKeyAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        const string QUERY = "SELECT `id`, `account_id` AS `AccountId`, `allowed_client_id` AS `AllowedClientId`, `allowed_scope` AS `AllowedScope`, `name`, `created_at` AS `CreatedAt` FROM `client_api_key` WHERE `id` = @id AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<ApiKeyInfo>(command);
        if (result == default)
        {
            return null;
        }

        return result;
    }

    public async ValueTask RemoveApiKeyAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        const string QUERY = "UPDATE `client_api_key` SET `removed_at` = NOW() WHERE `id` = @id AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask<ApiKeyInfo?> VerifyApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (!apiKey.StartsWith(ApiKeyPrefix))
        {
            return null;
        }

        var keyBody = apiKey[ApiKeyPrefix.Length..];
        if (keyBody.Length < KeyPrefixLength)
        {
            return null;
        }

        var keyPrefix = keyBody[..KeyPrefixLength];

        using var connection = GetConnection();
        const string QUERY = "SELECT `id`, `account_id` AS `AccountId`, `allowed_client_id` AS `AllowedClientId`, `allowed_scope` AS `AllowedScope`, `name`, `key_hash` AS `KeyHash`, `created_at` AS `CreatedAt` FROM `client_api_key` WHERE `key_prefix` = @keyPrefix AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { keyPrefix }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ApiKeyRecord>(command);

        foreach (var result in results)
        {
            if (PasswordHasher.Verify(apiKey, result.KeyHash))
            {
                return new ApiKeyInfo(result.Id, result.AccountId, result.AllowedClientId, result.AllowedScope, result.Name, result.CreatedAt);
            }
        }

        return null;
    }

    private record struct ApiKeyRecord(long Id, string AccountId, string? AllowedClientId, string? AllowedScope, string Name, string KeyHash, DateTime CreatedAt);
}
