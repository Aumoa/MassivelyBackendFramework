namespace OAuth2.DTO;

public record struct OAuthGrantInfo(string ClientId, string ClientName, string[] Scopes, DateTime GrantedAt);
