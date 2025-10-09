namespace OAuth2.DTO;

public record struct ClientInfo(string Id, string OwnerId, string Name, string[] RedirectUris, DateTime CreatedAt);
