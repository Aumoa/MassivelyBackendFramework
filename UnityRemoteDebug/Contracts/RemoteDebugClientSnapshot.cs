namespace UnityRemoteDebug.Contracts;

public sealed record RemoteDebugClientSnapshot(
    string ClientId,
    string DisplayName,
    string ClientVersion,
    string UnityVersion,
    string Capabilities,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeenAt);
