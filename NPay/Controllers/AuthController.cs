using Microsoft.AspNetCore.Mvc;
using OpenIDConnect;

namespace NPay.Controllers;

[Route("auth")]
public class AuthController(IAuthenticationStateProvider auth, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("redirect")]
    public async ValueTask<IActionResult> RedirectAsync([FromQuery] string code, CancellationToken cancellationToken)
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

        await auth.AcceptAsync(code, uri, cancellationToken);
        return Redirect("/");
    }
}
