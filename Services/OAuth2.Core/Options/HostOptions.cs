namespace OAuth2.Options;

public record HostOptions
{
    public required string Uri { get; init; }

    public required string ClientId { get; init; }
}
