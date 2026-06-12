using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed record ServiceConnectionCredentialInfo(
    long Id,
    MasterNodeKind NodeKind,
    string NodeId,
    string DisplayName,
    bool Enabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);
