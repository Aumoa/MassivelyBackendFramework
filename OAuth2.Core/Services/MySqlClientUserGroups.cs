using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlClientUserGroups(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IClientUserGroups
{
    public async ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `group`, `created_at` AS `createdAt` FROM `client_user_group` WHERE `client_id` = @id AND `account_id` = @accountId AND `removed_at` IS NULL";
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

    public async ValueTask<ClientUserGroup[]> GetAllClientUserGroupsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `id`, `client_id` AS `clientId`, `account_id` AS `accountId`, `group`, `created_at` AS `createdAt`, `removed_at` AS `removedAt`
            FROM `client_user_group`
            WHERE `client_id` = @clientId AND `removed_at` IS NULL
            ORDER BY `account_id`, `group`";
        
        var command = new CommandDefinition(QUERY, new { clientId }, cancellationToken: cancellationToken);
        var result = await connection.QueryAsync<ClientUserGroup>(command);
        return [.. result];
    }

    public async ValueTask AddClientUserGroupAsync(string clientId, string accountId, string group, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            INSERT INTO `client_user_group` (`client_id`, `account_id`, `group`)
            VALUES (@clientId, @accountId, @group)";
        
        var command = new CommandDefinition(QUERY, new { clientId, accountId, group }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask ModifyClientUserGroupAsync(long id, string newGroup, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            UPDATE `client_user_group`
            SET `group` = @newGroup
            WHERE `id` = @id AND `removed_at` IS NULL";
        
        var command = new CommandDefinition(QUERY, new { id, newGroup }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveClientUserGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            UPDATE `client_user_group`
            SET `removed_at` = NOW()
            WHERE `id` = @id AND `removed_at` IS NULL";
        
        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
