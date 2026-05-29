using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace MasterServer.Controllers;

[Route("auth")]
public sealed class AuthController(IAuthenticationStateProvider auth, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("redirect")]
    public async ValueTask<IActionResult> RedirectAsync([FromQuery] string code, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Authorization code is required.");
        }

        var redirectUri = UriHelper.BuildAbsolute(
            HttpContext.Request.Scheme,
            HttpContext.Request.Host,
            HttpContext.Request.PathBase,
            HttpContext.Request.Path);

        logger.LogInformation("Processing OIDC redirect for {RedirectUri}.", redirectUri);
        await auth.AcceptAsync(code, redirectUri, state, cancellationToken);
        return Redirect("/");
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        DeleteTokenCookie("id_token");
        DeleteTokenCookie("refresh_token");
        auth.Clear();

        return Redirect("/");
    }

    private void DeleteTokenCookie(string name)
    {
        HttpContext.Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }
}
