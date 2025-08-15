using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace OAuth2.Services;

internal class MySqlAccountsAndAccesses(IOptions<MySqlAccountsAndAccesses.Configuration> Options, PasswordHash PwHash) : IAccounts, IAccesses
{
    public record Configuration
    {
        public required string ConnectionString { get; init; }
        public required TimeSpan AccessTimeout { get; init; }
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

    public async ValueTask<bool> AcceptAsync(string id, string password, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY1 = "SELECT `password` FROM `accounts` WHERE `id` = @id";
        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        var hss = await connection.QuerySingleOrDefaultAsync<string>(command);
        if (string.IsNullOrEmpty(hss))
        {
            return false;
        }

        var parts = hss.Split('$');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Invalid password hash format.");
        }

        var (hash, salt) = (parts[0], parts[1]);
        return PwHash.VerifyPassword(password, hash, salt);
    }

    public async ValueTask<string> GetAccessAsync(string id, string scope, TimeSpan expireTime, CancellationToken cancellationToken)
    {
        string access_token = Guid.NewGuid().ToString();
        DateTime expires_at = DateTime.UtcNow.Add(expireTime);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string QUERY1 = "INSERT INTO `accesses` (`id`, `access_token`, `created_at`, `expires_at`) VALUES(@id, @access_token, NOW(), @expires_at)";
        var command = new CommandDefinition(QUERY1, new { id, access_token, expires_at }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        var scopeArray = scope.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in scopeArray)
        {
            const string QUERY2 = "INSERT INTO `access_scopes` (`access_token`, `scope`) VALUES(@access_token, @scope)";
            command = new CommandDefinition(QUERY2, new { access_token, scope = s }, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
        }

        await transaction.CommitAsync(cancellationToken);
        return access_token;
    }

    public async ValueTask<string[]> QueryRolesAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY1 = "SELECT `scope` FROM `access_scopes` WHERE `access_token` = @accessToken";
        var command = new CommandDefinition(QUERY1, new { accessToken }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<string>(command);

        return [.. results];
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(Options.Value.ConnectionString);
    }
}
