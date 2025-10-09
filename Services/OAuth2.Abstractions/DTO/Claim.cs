namespace OAuth2.DTO;

public record struct Claim(long Id, ClaimName Name, string Value);
