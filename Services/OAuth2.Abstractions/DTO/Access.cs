namespace OAuth2.DTO;

public record struct Access
{
    public required string AccessToken { get; set; }

    public required string RefreshToken { get; set; }
}
