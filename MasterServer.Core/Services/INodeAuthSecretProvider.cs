using MasterServer.ControlPlane;

namespace MasterServer.Services;

internal interface INodeAuthSecretProvider
{
    void EnsureConfigured();

    ValueTask<NodeAuthSecret?> GetSharedSecretAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        CancellationToken cancellationToken);

    ValueTask<bool> IsCredentialCurrentAsync(
        MasterNodeKind nodeKind,
        string nodeId,
        string credentialVersion,
        CancellationToken cancellationToken);
}

internal sealed record NodeAuthSecret(
    string SharedSecret,
    string CredentialVersion);
