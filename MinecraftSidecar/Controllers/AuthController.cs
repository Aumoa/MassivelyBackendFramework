using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace MinecraftSidecar.Controllers;

[Route("auth")]
public class AuthController(IAuthenticationStateProvider auth, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("redirect")]
    public async ValueTask<IActionResult> RedirectAsync([FromQuery] string code, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Authorization code is required.");
        }

        string uri = "https://" + HttpContext.Request.Host + HttpContext.Request.Path;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Redirecting with authorization code: {Code}, URI: {Uri}", code, uri);
        }
        await auth.AcceptAsync(code, uri, state, cancellationToken);
        return Redirect("/");
    }
}
