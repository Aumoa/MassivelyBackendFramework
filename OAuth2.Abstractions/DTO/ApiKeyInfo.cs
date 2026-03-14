namespace OAuth2.DTO;

public record struct ApiKeyInfo(long Id, string AccountId, string ClientId, string Name, DateTime CreatedAt);
