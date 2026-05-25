using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace OpenAI.Controllers;

[Route("auth")]
public class AuthController : ControllerBase
{
    [HttpGet("logout")]
    public ValueTask<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Cookies.Delete("id_token");
        HttpContext.Response.Cookies.Delete("refresh_token");
        return ValueTask.FromResult<IActionResult>(Redirect("/"));
    }

    [HttpGet("redirect")]
    public async ValueTask<IActionResult> RedirectAsync(
        [FromQuery] string code,
        [FromQuery] string? state,
        [FromServices] IAuthenticationStateProvider authState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Authorization code is required.");
        }

        await authState.AcceptAsync(code, HttpContext.Request.Scheme + "://" + HttpContext.Request.Host + HttpContext.Request.Path, state, cancellationToken);
        return Redirect("/");
    }
}
