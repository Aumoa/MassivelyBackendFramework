namespace OAuth2.DTO;

public record CreateApiKeyRequest
{
    public required string ClientId { get; init; }

    public required string Name { get; init; }
}
