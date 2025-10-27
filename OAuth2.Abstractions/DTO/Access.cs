namespace OAuth2.DTO;

public record struct Access
{
    public required string Id { get; set; }

    public required string AccessToken { get; set; }

    public required string RefreshToken { get; set; }

    public required string Scope { get; set; }

    public required string ClientId { get; set; }
}
