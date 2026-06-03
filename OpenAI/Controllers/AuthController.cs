using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace OpenAI.Controllers;

[Route("auth")]
public class AuthController(IAuthenticationStateProvider auth) : ControllerBase
{
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        auth.ClearTokenCookies(HttpContext);
        return Redirect("/");
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
