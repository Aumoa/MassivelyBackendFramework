using System.Security.Cryptography;
using Dapper;
using MasterServer.ControlPlane;
using MasterServer.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace MasterServer.Services;

internal sealed class MySqlNodeAuthSecretProvider(
    IOptions<ServiceConnectionCredentialOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<MySqlNodeAuthSecretProvider> logger) : INodeAuthSecretProvider
{
    private const string ProtectorPurpose = "MasterServer.ServiceConnectionCredentials.v1";

    private readonly ServiceConnectionCredentialOptions m_Options = options.Value;
    private readonly IDataProtector m_SecretProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_Options.User))
        {
            throw new InvalidOperationException("ServiceConnectionCredentials:User must be configured when service credentials are enabled.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.Password))
        {
            throw new InvalidOperationException("ServiceConnectionCredentials:Password must be configured when service credentials are enabled.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.DataProtectionApplicationName))
        {
            throw new InvalidOperationException("ServiceConnectionCredentials:DataProtectionApplicationName must be configured when service credentials are enabled.");
        }
    }

    public async ValueTask<string?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        await using var connection = new MySqlConnection(m_Options.ConnectionString);
        const string QUERY = """
SELECT `protected_secret`
FROM `service_connection_credential`
WHERE `node_kind` = @nodeKind
  AND `node_id` = @nodeId
  AND `enabled` = 1
  AND `removed_at` IS NULL
LIMIT 1;
""";
        var command = new CommandDefinition(
            QUERY,
            new
            {
                nodeKind = (byte)nodeKind,
                nodeId
            },
            cancellationToken: cancellationToken);
        var protectedSecret = await connection.QuerySingleOrDefaultAsync<string?>(command).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        try
        {
            return m_SecretProtector.Unprotect(protectedSecret);
        }
        catch (CryptographicException e)
        {
            logger.LogError(
                e,
                "Failed to unprotect service connection credential. NodeKind={NodeKind}, NodeId={NodeId}.",
                nodeKind,
                nodeId);
            return null;
        }
    }
}
