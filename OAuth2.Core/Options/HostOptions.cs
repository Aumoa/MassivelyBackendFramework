namespace OAuth2.Options;

public record HostOptions
{
    public required string ClientId { get; init; }

    public required string Secret { get; init; }
}
