using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlAccounts(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IAccounts
{
    public async ValueTask<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT 1 FROM `account` WHERE `id` = @id";
        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        int? result = await connection.QueryFirstOrDefaultAsync<int?>(command);
        return result == 1;
    }

    public async ValueTask<bool> VerifyAsync(string id, string password, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `password` FROM `account` WHERE `id` = @id";

        var command = new CommandDefinition(QUERY1, new { id, password = PasswordHasher.Hash(password) }, cancellationToken: cancellationToken);
        string? saved = await connection.QuerySingleOrDefaultAsync<string>(command);
        return saved != null && PasswordHasher.Verify(password, saved);
    }

    public async ValueTask AddAsync(string id, string password, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "INSERT INTO `account` (`id`, `password`, `sub`) VALUES(@id, @password, @sub)";

        var command = new CommandDefinition(QUERY1, new { id, password = PasswordHasher.Hash(password), sub = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "DELETE FROM `account` WHERE `id` = @id";

        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask<string?> GetSubAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `sub` FROM `account` WHERE `id` = @id";

        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(command);
    }
}
