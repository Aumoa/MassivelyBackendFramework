using Microsoft.AspNetCore.Mvc;

namespace OAuth2.DTO;

public record CreateClientRequest
{
    public required string Name { get; init; }

    [FromForm(Name = "client_id")]
    public string? ClientId { get; init; }
}
