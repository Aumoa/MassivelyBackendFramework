using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlClientUserGroups(ILogger<MySqlClientUserGroups> logger, IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IClientUserGroups
{
    public async ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        logger.LogInformation("Query {Id} {AccountId}", id, accountId);

        const string QUERY1 = "SELECT `group`, `created_at` AS `createdAt` FROM `client_user_group` WHERE `client_id` = @id AND `account_id` = @accountId";
        var command = new CommandDefinition(QUERY1, new { id, accountId }, cancellationToken: cancellationToken);
        (string group, DateTime createdAt)[] result = [.. await connection.QueryAsync<(string group, DateTime createdAt)>(command)];
        if (result.Length == 0)
        {
            return [];
        }

        var createdAt = result.Min(r => r.createdAt);
        return [new AccountClaim
        {
            Name = "groups",
            Value = $"[{string.Join(',', result.Select(r => $"\"{r.group}\""))}]",
            CreatedAt = createdAt
        }];
    }
}
