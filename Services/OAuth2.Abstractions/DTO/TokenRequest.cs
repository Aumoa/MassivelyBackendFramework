using Microsoft.AspNetCore.Mvc;

namespace OAuth2.DTO;

public record TokenRequest
{
    [FromForm(Name = "grant_type")]
    public required string GrantType { get; set; }

    [FromForm(Name = "code")]
    public required string Code { get; set; }

    [FromForm(Name = "redirect_uri")]
    public required string RedirectUri { get; set; }

    [FromForm(Name = "client_id")]
    public required string ClientId { get; set; }
}
