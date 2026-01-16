namespace OAuth2.Options;

internal record JwtOptions
{
    public required string Issuer { get; init; }

    public required TimeSpan ExpiresIn { get; init; }

    public required TimeSpan RefreshTokenExpiresIn { get; init; }

    public required string PrivateKeyPath { get; init; }

    public required string PublicKeyPath { get; init; }
}
