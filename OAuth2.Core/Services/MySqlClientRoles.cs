using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlClientRoles(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IClientRoles
{
    public async ValueTask<AccountClaim[]> GetAccountRolesAsync(string clientId, string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `cra`.`role_id` AS `roleId`, `cra`.`created_at` AS `createdAt`
            FROM `client_role_assignment` `cra`
            INNER JOIN `client` `c` ON `c`.`id` = `cra`.`client_id` AND `c`.`removed_at` IS NULL
            WHERE `cra`.`client_id` = @clientId AND `cra`.`account_id` = @accountId";
        var command = new CommandDefinition(QUERY, new { clientId, accountId }, cancellationToken: cancellationToken);
        (string roleId, DateTime createdAt)[] result = [.. await connection.QueryAsync<(string roleId, DateTime createdAt)>(command)];
        if (result.Length == 0)
        {
            return [];
        }

        var createdAt = result.Min(r => r.createdAt);
        return [new AccountClaim
        {
            Name = "roles",
            Value = $"[{string.Join(',', result.Select(r => $"\"{r.roleId}\""))}]",
            CreatedAt = createdAt
        }];
    }

    public async ValueTask<ClientRole[]> GetClientRolesAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `client_id` AS `clientId`, `id`, `name`, `created_at` AS `createdAt`
            FROM `client_role`
            WHERE `client_id` = @clientId
            ORDER BY `name`";
        var command = new CommandDefinition(QUERY, new { clientId }, cancellationToken: cancellationToken);
        var result = await connection.QueryAsync<ClientRole>(command);
        return [.. result];
    }

    public async ValueTask<ClientRoleAssignment[]> GetClientRoleAssignmentsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `cra`.`client_id` AS `clientId`, `cra`.`role_id` AS `roleId`, `cr`.`name` AS `roleName`, `cra`.`account_id` AS `accountId`, `cra`.`created_at` AS `createdAt`
            FROM `client_role_assignment` `cra`
            INNER JOIN `client_role` `cr` ON `cr`.`client_id` = `cra`.`client_id` AND `cr`.`id` = `cra`.`role_id`
            WHERE `cra`.`client_id` = @clientId
            ORDER BY `cra`.`account_id`, `cr`.`name`";
        var command = new CommandDefinition(QUERY, new { clientId }, cancellationToken: cancellationToken);
        var result = await connection.QueryAsync<ClientRoleAssignment>(command);
        return [.. result];
    }

    public async ValueTask AddClientRoleAsync(string clientId, string roleId, string name, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            INSERT INTO `client_role` (`client_id`, `id`, `name`)
            VALUES (@clientId, @roleId, @name)";
        var command = new CommandDefinition(QUERY, new { clientId, roleId, name }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveClientRoleAsync(string clientId, string roleId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            DELETE FROM `client_role`
            WHERE `client_id` = @clientId AND `id` = @roleId";
        var command = new CommandDefinition(QUERY, new { clientId, roleId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask AssignRoleAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            INSERT IGNORE INTO `client_role_assignment` (`client_id`, `role_id`, `account_id`)
            VALUES (@clientId, @roleId, @accountId)";
        var command = new CommandDefinition(QUERY, new { clientId, roleId, accountId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveRoleAssignmentAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            DELETE FROM `client_role_assignment`
            WHERE `client_id` = @clientId AND `role_id` = @roleId AND `account_id` = @accountId";
        var command = new CommandDefinition(QUERY, new { clientId, roleId, accountId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
