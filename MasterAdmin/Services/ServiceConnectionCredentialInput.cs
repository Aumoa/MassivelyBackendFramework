using MasterServer.ControlPlane;

namespace MasterAdmin.Services;

public sealed record ServiceConnectionCredentialInput(
    MasterNodeKind NodeKind,
    string NodeId,
    string DisplayName,
    bool Enabled);
