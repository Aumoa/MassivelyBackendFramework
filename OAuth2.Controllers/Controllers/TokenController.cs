using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/token")]
public class TokenController(IAuthorizationCodes authorizationCodes, IAccesses accesses) : ControllerBase
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] TokenRequest request, CancellationToken cancellationToken)
    {
        if (request.GrantType != "authorization_code")
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            return BadRequest(new { error = "invalid_grant" });
        }

        if (code.Value.RedirectUri == request.RedirectUri == false)
        {
            return BadRequest(new { error = "invalid_grant" });
        }

        var expiresIn = TimeSpan.FromHours(1);
        var access = await accesses.WriteAccessAsync(code.Value.AccountId, expiresIn, cancellationToken);
        var response = new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)expiresIn.TotalSeconds,
            Scope = code.Value.Scope,
            RefreshToken = access.RefreshToken
        };

        return Ok(response);
    }
}
