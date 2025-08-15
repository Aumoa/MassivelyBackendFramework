namespace OAuth2.DTO;

public record AccountRecord
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }
}
