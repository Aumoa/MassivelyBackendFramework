using Auth.DTO;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace Auth.Services;

internal class MySqlAccesses(
    IOptions<MySqlAccesses.Configuration> options,
    ISelfProviderAccessCode selfProviderAccess,
    JwtTokenGenerator jwt
    ) : IAccesses
{
    public record Configuration : IAccesses.Configuration
    {
        public required string ConnectionString { get; init; }
    }

    public async ValueTask<string?> GetAccessAsync(string provider, string code, CancellationToken cancellationToken)
    {
        AccountRecord? account;

        switch (provider)
        {
            case "self-provide":
                account = await selfProviderAccess.GetIdAsync(code, cancellationToken);
                break;
            default:
                return null;
        }

        if (account == null)
        {
            return null;
        }

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        string accessToken = Guid.NewGuid().ToString();
        string email = account.Email;
        const string QUERY1 =
            "INSERT INTO `accesses` VALUES('self-provide', @id, @accessToken, @id, @email, NOW(), NULL)" +
            "    ON DUPLICATE KEY UPDATE `access_token` = @accessToken, `last_login_date` = NOW(), `last_logout_date` = NULL";
        var command = new CommandDefinition(QUERY1, new { account.Id, accessToken, email }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        return jwt.Generate(account.Id, account.Name, account.Email, accessToken);
    }

    public async ValueTask LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY1 =
            "UPDATE `accesses`" +
            "    SET `access_token` = '', `last_logout_date` = NOW()" +
            "    WHERE `access_token` = @accessToken";

        var command = new CommandDefinition(QUERY1, new { accessToken }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }
}
