using System;

namespace MasterServer.Services;

public sealed class GatewayBackendRoutePolicyEntryInfo
{
    public GatewayBackendRoutePolicyEntryInfo(
        long id,
        string backendKind,
        bool enabled,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        BackendKind = backendKind ?? throw new ArgumentNullException(nameof(backendKind));
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public long Id { get; }

    public string BackendKind { get; }

    public bool Enabled { get; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }
}
