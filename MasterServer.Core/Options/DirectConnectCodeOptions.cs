namespace MasterServer.Options;

public sealed record DirectConnectCodeOptions
{
    public int TimeToLiveMilliseconds { get; init; } = 15000;

    public string RedisKeyPrefix { get; init; } = "mbf:direct-connect-code";
}
