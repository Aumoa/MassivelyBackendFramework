using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed class ServiceConnectionCredentialInput
{
    public ServiceConnectionCredentialInput(
        MasterNodeKind nodeKind,
        string nodeId,
        string displayName,
        string? backendKind,
        bool enabled)
    {
        NodeKind = nodeKind;
        NodeId = nodeId;
        DisplayName = displayName;
        BackendKind = backendKind;
        Enabled = enabled;
    }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string? BackendKind { get; }

    public bool Enabled { get; }
}
