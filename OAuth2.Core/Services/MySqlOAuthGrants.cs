using Dapper;
using Microsoft.Extensions.Options;
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
}
