using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlAccountClaims(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IAccountClaims
{
    public async ValueTask<AccountClaim[]> GetClaimsAsync(string accountId, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `id`, `name`, `value`, `created_at` AS `createdAt` FROM `account_claim` WHERE `account_id` = @accountId AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { accountId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<AccountClaim>(command);

        return [.. results];
    }

    public async ValueTask AddClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();

        const string QUERY1 = "INSERT INTO `account_claim` (`account_id`, `name`, `value`) VALUES(@accountId, @name, @value)";
        var command = new CommandDefinition(QUERY1, new { accountId, name, value }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask SetUniqueClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        const string QUERY1 = "UPDATE `account_claim` SET `removed_at` = NOW() WHERE `account_id` = @accountId AND `name` = @name AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { accountId, name }, transaction: tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        const string QUERY2 = "INSERT INTO `account_claim` (`account_id`, `name`, `value`) VALUES(@accountId, @name, @value);";
        command = new CommandDefinition(QUERY2, new { accountId, name, value }, transaction: tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        await tx.CommitAsync(cancellationToken);
    }

    public async ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();

        const string QUERY1 = "UPDATE `account_claim` SET `removed_at` = NOW() WHERE `id` = @id AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveClaimsAsync(string accountId, string name, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        var name_s = name.ToString();
        const string QUERY1 = "UPDATE `account_claim` SET `removed_at` = NOW() WHERE `account_id` = @accountId AND `name` = @name_s AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { accountId, name_s }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
