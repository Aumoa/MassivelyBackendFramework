namespace OAuth2.DTO;

public record struct AccountClaim(long Id, string Name, string Value, DateTime CreatedAt);
