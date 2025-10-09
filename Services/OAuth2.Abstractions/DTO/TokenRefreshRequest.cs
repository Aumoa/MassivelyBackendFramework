namespace OAuth2.DTO;

public record TokenRefreshRequest
{
    public required string RefreshToken { get; set; }
}
