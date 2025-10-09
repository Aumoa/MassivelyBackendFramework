using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/token")]
public class TokenController(IAuthorizationCodes authorizationCodes, IAccesses accesses) : ControllerBase
{
    private static readonly TimeSpan ExpiresIn = TimeSpan.FromHours(1);

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

        var access = await accesses.WriteAccessAsync(code.Value.AccountId, code.Value.Scope, ExpiresIn, cancellationToken);
        var response = new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)ExpiresIn.TotalSeconds,
            Scope = access.Scope,
            RefreshToken = access.RefreshToken
        };

        return Ok(response);
    }

    [HttpPost("refresh")]
    public async ValueTask<IActionResult> RefreshAsync([FromForm] TokenRefreshRequest request, CancellationToken cancellationToken)
    {
        var newAccess = await accesses.RefreshAccessAsync(request.RefreshToken, ExpiresIn, cancellationToken);
        if (newAccess.HasValue == false)
        {
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        var response = new TokenResponse
        {
            AccessToken = newAccess.Value.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)ExpiresIn.TotalSeconds,
            Scope = newAccess.Value.Scope,
            RefreshToken = newAccess.Value.RefreshToken
        };

        return Ok(response);
    }
}
