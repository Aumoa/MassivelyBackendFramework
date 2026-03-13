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

        const string QUERY1 = @"
            SELECT `cug`.`group`, `cug`.`created_at` AS `createdAt`
            FROM `client_user_group` `cug`
            INNER JOIN `client` `c` ON `c`.`id` = `cug`.`client_id` AND `c`.`removed_at` IS NULL
            WHERE `cug`.`client_id` = @id AND `cug`.`account_id` = @accountId AND `cug`.`removed_at` IS NULL";
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
            SELECT `cug`.`id`, `cug`.`client_id` AS `clientId`, `cug`.`account_id` AS `accountId`, COALESCE(`a`.`id`, `cug`.`account_id`) AS `accountLoginId`, `cug`.`group`, `cug`.`created_at` AS `createdAt`, `cug`.`removed_at` AS `removedAt`
            FROM `client_user_group` `cug`
            LEFT JOIN `account` `a` ON `a`.`sub` = `cug`.`account_id`
            WHERE `cug`.`client_id` = @clientId AND `cug`.`removed_at` IS NULL
            ORDER BY `cug`.`account_id`, `cug`.`group`";
        
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
