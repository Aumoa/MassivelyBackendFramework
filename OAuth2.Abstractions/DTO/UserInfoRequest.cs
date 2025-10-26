using Microsoft.AspNetCore.Mvc;

namespace OAuth2.DTO;

public record UserInfoRequest
{
    [FromForm(Name = "access_token")]
    public string? AccessToken { get; set; }
}
