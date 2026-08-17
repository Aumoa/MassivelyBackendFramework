namespace OAuth2.DTO;

public record struct AccountRole(
    long Id,
    string AccountId,
    string AccountLoginId,
    string Name,
    DateTime CreatedAt,
    DateTime? RemovedAt
);
