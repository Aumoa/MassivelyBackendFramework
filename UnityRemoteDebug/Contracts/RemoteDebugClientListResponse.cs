namespace UnityRemoteDebug.Contracts;

public sealed record RemoteDebugClientListResponse(
    IReadOnlyCollection<RemoteDebugClientSnapshot> Clients,
    DateTimeOffset GeneratedAt,
    string BackendKind,
    int GatewayConnectionCount,
    string? ErrorMessage);
