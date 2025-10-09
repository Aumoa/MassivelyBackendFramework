namespace OAuth2.Options;

public record RedisOptions
{
    public required string ConnectionString { get; init; }

    public int DbIndex { get; init; } = 0;
}
