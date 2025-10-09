namespace OAuth2.Options;

public record JwtOptions
{
    public required string Salt { get; init; }
}
