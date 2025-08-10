using Auth.DTO;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Scripting.DTO;

namespace Auth.Services;

internal class MySqlAccounts(IOptions<MySqlAccounts.Configuration> options, PasswordHash pwhash) : IAccounts
{
    public record Configuration
    {
        public required string ConnectionString { get; init; }
    }

    public async ValueTask<bool> ContainsAsync(string id, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY1 = "SELECT COUNT(*) FROM `accounts` WHERE `id` = @id";
        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        var count = await connection.QuerySingleAsync<int>(command);
        return count > 0;
    }

    public async ValueTask<ResponseCode> RegisterAsync(string id, string password, string email, CancellationToken cancellationToken)
    {
        pwhash.HashPassword(password, out var hash, out var salt);
        var hss = $"{hash}${salt}";

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync();
        const string QUERY1 = "SELECT COUNT(*) FROM `accounts` WHERE `email` = @email";
        var command = new CommandDefinition(QUERY1, new { email }, cancellationToken: cancellationToken);
        var exists = await connection.QuerySingleAsync<int>(command) > 0;
        if (exists)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResponseCode.AccountEmailDuplicated;
        }

        const string QUERY2 = "INSERT IGNORE INTO `accounts` VALUES(@id, @hss, @email, NOW())";
        command = new CommandDefinition(QUERY2, new { id, hss, email }, cancellationToken: cancellationToken);
        var result = await connection.ExecuteAsync(command) > 0;
        if (result == false)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResponseCode.AccountAlreadyRegistered;
        }

        await transaction.CommitAsync(cancellationToken);
        return ResponseCode.Success;
    }

    public async ValueTask<AccountRecord?> GetIdAsync(string code, CancellationToken cancellationToken)
    {
        var (id, password) = As2(code.Split('$', 2));

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string QUERY1 = "SELECT `password`, `email` FROM `accounts` WHERE `id` = @id";
        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        var account = await connection.QuerySingleOrDefaultAsync<dynamic>(command);
        if (account == null)
        {
            return null;
        }

        var (hash, salt) = As2((string[])account.password.Split('$', 2));
        if (pwhash.VerifyPassword(password, hash, salt) == false)
        {
            return null;
        }

        return new AccountRecord
        {
            Id = id,
            Name = id,
            Email = account.email
        };
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static (string, string) As2(string[] values)
    {
        return (values[0], values[1]);
    }
}
