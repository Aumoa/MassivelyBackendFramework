namespace OAuth2.DTO;

public record CreateClientRequest
{
    public required string Name { get; init; }
}
