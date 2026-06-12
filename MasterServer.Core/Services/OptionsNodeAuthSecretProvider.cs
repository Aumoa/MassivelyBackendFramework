using System.Security.Cryptography;
using System.Text;
using MasterServer.ControlPlane;
using MasterServer.Options;
using Microsoft.Extensions.Options;

namespace MasterServer.Services;

internal sealed class OptionsNodeAuthSecretProvider(
    IOptions<MasterSocketOptions> options) : INodeAuthSecretProvider
{
    private readonly MasterSocketOptions m_Options = options.Value;

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_Options.NodeAuthSecret))
        {
            throw new InvalidOperationException("MasterSocket:NodeAuthSecret must be configured before accepting node connections.");
        }
    }

    public ValueTask<NodeAuthSecret?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<NodeAuthSecret?>(
            new NodeAuthSecret(m_Options.NodeAuthSecret, CreateVersion(m_Options.NodeAuthSecret)));
    }

    public ValueTask<bool> IsCredentialCurrentAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        string credentialVersion,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(string.Equals(
            credentialVersion,
            CreateVersion(m_Options.NodeAuthSecret),
            StringComparison.Ordinal));
    }

    private static string CreateVersion(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
