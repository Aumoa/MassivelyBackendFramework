using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlOAuthGrants(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IOAuthGrants
{
    public async ValueTask<string[]> GetGrantedScopesAsync(string accountId, string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `scope`
            FROM `oauth_grant`
            WHERE `account_id` = @accountId
                AND `client_id` = @clientId
                AND `revoked_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { accountId, clientId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<string>(command);
        return [.. results];
    }

    public async ValueTask<OAuthGrantInfo[]> GetGrantedClientsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT
                `g`.`client_id` AS `ClientId`,
                `c`.`name` AS `ClientName`,
                `g`.`scope` AS `Scope`,
                `g`.`updated_at` AS `GrantedAt`
            FROM `oauth_grant` `g`
            INNER JOIN `client` `c`
                ON `c`.`id` = `g`.`client_id`
                AND `c`.`removed_at` IS NULL
            WHERE `g`.`account_id` = @accountId
                AND `g`.`revoked_at` IS NULL
            ORDER BY `c`.`name`, `g`.`scope`";
        var command = new CommandDefinition(QUERY, new { accountId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<GrantRow>(command);

        return [.. rows
            .GroupBy(row => new { row.ClientId, row.ClientName })
            .Select(group => new OAuthGrantInfo(
                group.Key.ClientId,
                group.Key.ClientName,
                [.. group.Select(row => row.Scope).Where(scope => ScopePolicy.SupportedScopes.Contains(scope, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)],
                group.Max(row => row.GrantedAt)))];
    }

    public async ValueTask GrantScopesAsync(string accountId, string clientId, string scopes, CancellationToken cancellationToken = default)
    {
        var scopeValues = ScopePolicy.Split(scopes)
            .Where(scope => ScopePolicy.SupportedScopes.Contains(scope, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(scope => new { accountId, clientId, scope })
            .ToArray();

        if (scopeValues.Length == 0)
        {
            return;
        }

        using var connection = GetConnection();

        const string QUERY = @"
            INSERT INTO `oauth_grant` (`account_id`, `client_id`, `scope`)
            VALUES (@accountId, @clientId, @scope)
            ON DUPLICATE KEY UPDATE
                `revoked_at` = NULL,
                `updated_at` = NOW()";
        var command = new CommandDefinition(QUERY, scopeValues, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RevokeClientGrantsAsync(string accountId, string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            UPDATE `oauth_grant`
            SET `revoked_at` = NOW(),
                `updated_at` = NOW()
            WHERE `account_id` = @accountId
                AND `client_id` = @clientId
                AND `revoked_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { accountId, clientId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private sealed class GrantRow
    {
        public string ClientId { get; set; } = string.Empty;

        public string ClientName { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        public DateTime GrantedAt { get; set; }
    }
}
