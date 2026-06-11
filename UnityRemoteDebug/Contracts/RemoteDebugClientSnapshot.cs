namespace UnityRemoteDebug.Contracts;

public sealed record RemoteDebugClientSnapshot(
    Guid ClientId,
    string DisplayName,
    string ProjectName,
    string UnityVersion,
    string Platform,
    string RemoteEndPoint,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeenAt);
