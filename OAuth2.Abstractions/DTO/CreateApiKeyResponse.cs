namespace OAuth2.DTO;

public record CreateApiKeyResponse
{
    public required string ApiKey { get; set; }
}
