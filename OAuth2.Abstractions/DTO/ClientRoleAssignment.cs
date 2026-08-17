namespace OAuth2.DTO;

public record struct ClientRoleAssignment(string ClientId, string RoleId, string RoleName, string AccountId, DateTime CreatedAt);
