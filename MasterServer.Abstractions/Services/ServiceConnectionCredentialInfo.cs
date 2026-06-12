using System;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public sealed class ServiceConnectionCredentialInfo
{
    public ServiceConnectionCredentialInfo(
        long id,
        MasterNodeKind nodeKind,
        string nodeId,
        string displayName,
        string? backendKind,
        bool enabled,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        NodeKind = nodeKind;
        NodeId = nodeId;
        DisplayName = displayName;
        BackendKind = backendKind;
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public long Id { get; }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string? BackendKind { get; }

    public bool Enabled { get; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }
}
