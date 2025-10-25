using Dapper;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlClientClaims(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IClientClaims
{
    public async ValueTask<ClientClaim[]> GetClaimsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "SELECT `name`, `value` FROM `client_claim` WHERE `client_id` = @clientId AND `removed_at` IS NULL;";
        var command = new CommandDefinition(QUERY1, new { clientId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ClientClaim>(command);

        return [.. results];
    }

    public async ValueTask AddClaimAsync(string clientId, string name, string value, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY1 = "INSERT INTO `client_claim` (`client_id`, `name`, `value`) VALUES(@clientId, @name, @value)";
        var command = new CommandDefinition(QUERY1, new { clientId, name, value }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
