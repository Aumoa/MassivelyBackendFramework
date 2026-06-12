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

    public ValueTask<string?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<string?>(m_Options.NodeAuthSecret);
    }
}
