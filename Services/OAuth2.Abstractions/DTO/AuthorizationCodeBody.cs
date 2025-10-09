namespace OAuth2.DTO;

public record struct AuthorizationCodeBody(
    string AccountId,
    string ClientId,
    string Scope,
    string RedirectUri
    );
