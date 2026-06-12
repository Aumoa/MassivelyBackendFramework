using MasterServer.ControlPlane;

namespace MasterServer.Services;

internal interface INodeAuthSecretProvider
{
    void EnsureConfigured();

    ValueTask<string?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken);
}
