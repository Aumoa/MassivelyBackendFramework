namespace OAuth2.DTO;

public record struct UserInfo(
    string? Sub,
    string? Name,
    string? Email,
    string? Picture
    );
