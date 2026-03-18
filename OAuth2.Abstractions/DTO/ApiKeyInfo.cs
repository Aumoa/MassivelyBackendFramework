namespace OAuth2.DTO;

public record struct ApiKeyInfo(long Id, string AccountId, string? AllowedClientId, string? AllowedScope, string Name, DateTime CreatedAt);
