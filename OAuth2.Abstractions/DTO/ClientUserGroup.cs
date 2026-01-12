namespace OAuth2.DTO;

public record struct ClientUserGroup(
    long Id,
    string ClientId,
    string AccountId,
    string Group,
    DateTime CreatedAt,
    DateTime? RemovedAt
);
