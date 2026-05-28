namespace OAuth2.DTO;

public record struct AuthorizationCodeBody(
    string AccountId,
    string ClientId,
    string Scope,
    string RedirectUri,
    string? Nonce,
    string? CodeChallenge = null,
    string? CodeChallengeMethod = null,
    long? AuthTime = null,
    string? Acr = null
    );
