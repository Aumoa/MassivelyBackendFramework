using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed record ServiceConnectionCredentialInput(
    MasterNodeKind NodeKind,
    string NodeId,
    string DisplayName,
    string? BackendKind,
    bool Enabled);
