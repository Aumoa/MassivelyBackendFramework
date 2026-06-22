using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IGatewayBackendRoutePolicyProvider
{
    string[] GetAllowedBackendKinds();

    bool IsBackendKindAllowed(string backendKind, out string normalizedBackendKind);
}

internal interface IGatewayBackendRoutePolicyWriter
{
    void Publish(GatewayBackendRoutePolicySnapshot snapshot);
}

internal sealed class GatewayBackendRoutePolicyCatalog :
    IGatewayBackendRoutePolicyProvider,
    IGatewayBackendRoutePolicyWriter
{
    private readonly object m_Sync = new();
    private string[] m_AllowedBackendKinds = [];

    public string[] GetAllowedBackendKinds()
    {
        lock (m_Sync)
        {
            return [.. m_AllowedBackendKinds];
        }
    }

    public bool IsBackendKindAllowed(string backendKind, out string normalizedBackendKind)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            normalizedBackendKind = string.Empty;
            return false;
        }

        var normalized = backendKind.Trim();
        normalizedBackendKind = normalized;
        lock (m_Sync)
        {
            return m_AllowedBackendKinds.Any(candidate => string.Equals(
                candidate,
                normalized,
                StringComparison.Ordinal));
        }
    }

    public void Publish(GatewayBackendRoutePolicySnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var allowedBackendKinds = snapshot.AllowedBackendKinds
            .Where(static backendKind => !string.IsNullOrWhiteSpace(backendKind))
            .Select(static backendKind => backendKind.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static backendKind => backendKind, StringComparer.Ordinal)
            .ToArray();

        lock (m_Sync)
        {
            m_AllowedBackendKinds = allowedBackendKinds;
        }
    }
}
