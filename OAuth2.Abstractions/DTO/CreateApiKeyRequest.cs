namespace OAuth2.DTO;

public record CreateApiKeyRequest
{
    public required string Name { get; init; }

    public string? AllowedClientId { get; init; }

    public string? AllowedScope { get; init; }
}
