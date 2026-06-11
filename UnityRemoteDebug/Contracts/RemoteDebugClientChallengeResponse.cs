namespace UnityRemoteDebug.Contracts;

public sealed record RemoteDebugClientChallengeResponse(
    string ChallengeId,
    string Nonce,
    DateTimeOffset ExpiresAt,
    string Algorithm);
