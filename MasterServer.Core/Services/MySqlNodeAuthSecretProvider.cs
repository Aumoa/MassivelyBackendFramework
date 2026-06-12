using System.Security.Cryptography;
using System.Text;
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
    IOptions<MasterAdminConnectionOptions> masterAdminOptions,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<MySqlNodeAuthSecretProvider> logger) : INodeAuthSecretProvider
{
    private const string ProtectorPurpose = "MasterServer.ServiceConnectionCredentials.v1";

    private readonly ServiceConnectionCredentialOptions m_Options = options.Value;
    private readonly MasterAdminConnectionOptions m_MasterAdminOptions = masterAdminOptions.Value;
    private readonly IDataProtector m_SecretProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_MasterAdminOptions.NodeId))
        {
            throw new InvalidOperationException("MasterAdminConnection:NodeId must be configured for MasterAdmin node authentication.");
        }

        if (string.IsNullOrWhiteSpace(m_MasterAdminOptions.SharedSecret))
        {
            throw new InvalidOperationException("MasterAdminConnection:SharedSecret must be configured for MasterAdmin node authentication.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.User))
        {
            throw new InvalidOperationException("ServiceConnectionCredentials:User must be configured for Master node authentication.");
        }

        if (string.IsNullOrWhiteSpace(m_Options.DataProtectionApplicationName))
        {
            throw new InvalidOperationException("ServiceConnectionCredentials:DataProtectionApplicationName must be configured for Master node authentication.");
        }
    }

    public async ValueTask<NodeAuthSecret?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken)
    {
        if (nodeKind == MasterNodeKind.MasterAdmin)
        {
            return GetMasterAdminSharedSecret(nodeId);
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        var protectedSecret = await GetProtectedSecretAsync(nodeKind, nodeId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        try
        {
            return new NodeAuthSecret(
                m_SecretProtector.Unprotect(protectedSecret),
                CreateVersion(protectedSecret));
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

    public async ValueTask<bool> IsCredentialCurrentAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        string credentialVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialVersion))
        {
            return false;
        }

        if (nodeKind == MasterNodeKind.MasterAdmin)
        {
            return string.Equals(
                credentialVersion,
                GetMasterAdminCredentialVersion(nodeId),
                StringComparison.Ordinal);
        }

        var protectedSecret = await GetProtectedSecretAsync(nodeKind, nodeId, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(protectedSecret) &&
               string.Equals(credentialVersion, CreateVersion(protectedSecret), StringComparison.Ordinal);
    }

    private async ValueTask<string?> GetProtectedSecretAsync(
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
        return await connection.QuerySingleOrDefaultAsync<string?>(command).ConfigureAwait(false);
    }

    private NodeAuthSecret? GetMasterAdminSharedSecret(string nodeId)
    {
        if (!string.Equals(nodeId, m_MasterAdminOptions.NodeId, StringComparison.Ordinal))
        {
            return null;
        }

        return new NodeAuthSecret(
            m_MasterAdminOptions.SharedSecret,
            CreateConfiguredSecretVersion(MasterNodeKind.MasterAdmin, nodeId, m_MasterAdminOptions.SharedSecret));
    }

    private string GetMasterAdminCredentialVersion(string nodeId)
    {
        if (!string.Equals(nodeId, m_MasterAdminOptions.NodeId, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return CreateConfiguredSecretVersion(MasterNodeKind.MasterAdmin, nodeId, m_MasterAdminOptions.SharedSecret);
    }

    private static string CreateVersion(string protectedSecret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protectedSecret)));
    }

    private static string CreateConfiguredSecretVersion(MasterNodeKind nodeKind, string nodeId, string sharedSecret)
    {
        return CreateVersion($"{(byte)nodeKind}:{nodeId}:{sharedSecret}");
    }
}
