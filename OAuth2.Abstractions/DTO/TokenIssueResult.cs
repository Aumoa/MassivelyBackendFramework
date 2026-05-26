namespace OAuth2.DTO;

public readonly record struct TokenIssueResult(TokenResponse Response, Access Access);
