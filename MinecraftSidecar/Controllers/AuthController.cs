using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace MinecraftSidecar.Controllers;

[Route("auth")]
public class AuthController(IAuthenticationStateProvider auth) : ControllerBase
{
    [HttpGet("redirect")]
    public async ValueTask<IActionResult> RedirectAsync([FromQuery] string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Authorization code is required.");
        }

        await auth.AcceptAsync(code, HttpContext.Request.Scheme + "://" + HttpContext.Request.Host + HttpContext.Request.Path, cancellationToken);
        return Redirect("/");
    }
}
