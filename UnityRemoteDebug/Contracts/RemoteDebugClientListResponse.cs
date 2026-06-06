namespace UnityRemoteDebug.Contracts;

public sealed record RemoteDebugClientListResponse(
    IReadOnlyCollection<RemoteDebugClientSnapshot> Clients,
    DateTimeOffset GeneratedAt);
