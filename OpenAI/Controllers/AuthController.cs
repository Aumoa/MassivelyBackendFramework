using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace OpenAI.Controllers;

[ApiController]
public class AuthController : ControllerBase
{
    [HttpGet("/auth/logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Cookies.Delete("id_token");
        HttpContext.Response.Cookies.Delete("refresh_token");
        return Redirect("/");
    }

    [HttpGet("/auth/redirect")]
    public async Task<IActionResult> RedirectAsync(
        [FromQuery] string code,
        [FromServices] IAuthenticationStateProvider authState,
        CancellationToken cancellationToken)
    {
        await authState.AcceptAsync(code, HttpContext.Request.Scheme + "://" + HttpContext.Request.Host + HttpContext.Request.Path, cancellationToken);
        return Redirect("/");
    }
}
