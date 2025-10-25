using System.Security.Cryptography;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
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

    public async ValueTask<bool?> LoginAsync(string id, string password, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `password`, `verify_code` FROM `account` WHERE `id` = @id";

        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        var saved = await connection.QuerySingleOrDefaultAsync<(string pw, string? verified)?>(command);
        return saved == null ? null : PasswordHasher.Verify(password, saved.Value.pw) ? string.IsNullOrEmpty(saved.Value.verified) : null;
    }

    public async ValueTask<string> AddAsync(string id, string password, string name, string email, string verifyCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyCode);

        using var connection = GetConnection();

        const string QUERY1 = "INSERT INTO `account` (`id`, `password`, `sub`, `name`, `email`, `verify_code`) VALUES(@id, @password, @sub, @name, @email, @verifyPassword)";

        var verifyPassword = PasswordHasher.Hash(verifyCode);
        var sub = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var command = new CommandDefinition(QUERY1, new { id, password = PasswordHasher.Hash(password), sub, name, email, verifyPassword }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        return sub;
    }

    public async ValueTask<bool> RefreshVerifyCodeAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyCode);

        using var connection = GetConnection();

        const string QUERY1 = "UPDATE `account` SET `verify_code` = @verifyPassword WHERE `sub` = @sub AND `verify_code` IS NOT NULL";

        var verifyPassword = PasswordHasher.Hash(verifyCode);
        var command = new CommandDefinition(QUERY1, new { sub, verifyPassword }, cancellationToken: cancellationToken);
        int aff = await connection.ExecuteAsync(command);

        return aff == 1;
    }

    public async ValueTask<bool> VerifyAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyCode);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string QUERY1 = "SELECT `verify_code` FROM `account` WHERE `sub` = @sub";
        var command = new CommandDefinition(QUERY1, new { sub }, transaction: transaction, cancellationToken: cancellationToken);
        var verifySaved = await connection.QuerySingleOrDefaultAsync<string?>(command);
        if (verifySaved == null)
        {
            return true;
        }

        if (PasswordHasher.Verify(verifyCode, verifySaved) == false)
        {
            return false;
        }

        const string QUERY2 = "UPDATE `account` SET `verify_code` = NULL WHERE `sub` = @sub";
        command = new CommandDefinition(QUERY2, new { sub }, transaction: transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<RawAccount?> GetRawAccountAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `sub`, `name`, `email`, `created_at` FROM `account` WHERE `id` = @id";

        var command = new CommandDefinition(QUERY1, new { id }, cancellationToken: cancellationToken);
        var (sub, name, email, created_at) = await connection.QuerySingleOrDefaultAsync<(string sub, string name, string email, DateTime created_at)>(command);
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        return new RawAccount
        {
            Sub = sub,
            Name = name,
            Email = email,
            CreatedAt = created_at
        };
    }
}
