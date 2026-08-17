using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlAccountRoles(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IAccountRoles
{
    public async ValueTask<AccountClaim[]> GetAccountRolesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `name`, `created_at` AS `createdAt`
            FROM `account_role`
            WHERE `account_id` = @accountId AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { accountId }, cancellationToken: cancellationToken);
        (string name, DateTime createdAt)[] result = [.. await connection.QueryAsync<(string name, DateTime createdAt)>(command)];
        if (result.Length == 0)
        {
            return [];
        }

        var createdAt = result.Min(r => r.createdAt);
        return [new AccountClaim
        {
            Name = "roles",
            Value = $"[{string.Join(',', result.Select(r => $"\"{r.name}\""))}]",
            CreatedAt = createdAt
        }];
    }

    public async ValueTask<bool> HasRoleAsync(string accountId, string role, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT 1
            FROM `account_role`
            WHERE `account_id` = @accountId AND `name` = @role AND `removed_at` IS NULL
            LIMIT 1";
        var command = new CommandDefinition(QUERY, new { accountId, role }, cancellationToken: cancellationToken);
        var result = await connection.QueryFirstOrDefaultAsync<int?>(command);
        return result.HasValue;
    }

    public async ValueTask<AccountRole[]> GetAllAccountRolesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            SELECT `ar`.`id`, `ar`.`account_id` AS `accountId`, COALESCE(`a`.`id`, `ar`.`account_id`) AS `accountLoginId`, `ar`.`name`, `ar`.`created_at` AS `createdAt`, `ar`.`removed_at` AS `removedAt`
            FROM `account_role` `ar`
            LEFT JOIN `account` `a` ON `a`.`sub` = `ar`.`account_id`
            WHERE `ar`.`removed_at` IS NULL
            ORDER BY `ar`.`account_id`, `ar`.`name`";

        var command = new CommandDefinition(QUERY, cancellationToken: cancellationToken);
        var result = await connection.QueryAsync<AccountRole>(command);
        return [.. result];
    }

    public async ValueTask AddAccountRoleAsync(string accountId, string role, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            INSERT INTO `account_role` (`account_id`, `name`)
            VALUES (@accountId, @role)";

        var command = new CommandDefinition(QUERY, new { accountId, role }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask ModifyAccountRoleAsync(long id, string newRole, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            UPDATE `account_role`
            SET `name` = @newRole
            WHERE `id` = @id AND `removed_at` IS NULL";

        var command = new CommandDefinition(QUERY, new { id, newRole }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveAccountRoleAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
            UPDATE `account_role`
            SET `removed_at` = NOW()
            WHERE `id` = @id AND `removed_at` IS NULL";

        var command = new CommandDefinition(QUERY, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
