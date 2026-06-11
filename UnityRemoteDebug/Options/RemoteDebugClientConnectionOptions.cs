namespace UnityRemoteDebug.Options;

public sealed class RemoteDebugClientConnectionOptions
{
    public string SharedSecret { get; set; } = string.Empty;

    public int ChallengeLifetimeSeconds { get; set; } = 60;

    public int MaxConcurrentClients { get; set; } = 32;

    public int MaxConcurrentClientsPerRemoteEndPoint { get; set; } = 4;

    public int IdleTimeoutSeconds { get; set; } = 60;

    public int LastSeenNotificationIntervalSeconds { get; set; } = 5;

    internal TimeSpan ChallengeLifetime => TimeSpan.FromSeconds(Math.Max(1, ChallengeLifetimeSeconds));

    internal TimeSpan IdleTimeout => TimeSpan.FromSeconds(Math.Max(1, IdleTimeoutSeconds));

    internal TimeSpan LastSeenNotificationInterval => TimeSpan.FromSeconds(Math.Max(1, LastSeenNotificationIntervalSeconds));

    internal int EffectiveMaxConcurrentClients => Math.Max(1, MaxConcurrentClients);

    internal int EffectiveMaxConcurrentClientsPerRemoteEndPoint => Math.Max(1, MaxConcurrentClientsPerRemoteEndPoint);
}
