using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace OpenIDConnect.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("_oidc")]
public sealed class OidcLoginController(IAuthenticationStateProvider auth) : ControllerBase
{
    [HttpGet("login")]
    public IActionResult Login(
        [FromQuery(Name = "redirect_uri")] string redirectUri,
        [FromQuery] string scope)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return BadRequest("redirect_uri is required.");
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return BadRequest("scope is required.");
        }

        if (!IsSameOriginRedirectUri(Request, redirectUri))
        {
            return BadRequest("redirect_uri must be on the current origin.");
        }

        return Redirect(auth.GenerateLoginUri(redirectUri, scope));
    }

    private static bool IsSameOriginRedirectUri(HttpRequest request, string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathBase = request.PathBase.Value;
        return string.IsNullOrEmpty(pathBase) ||
               uri.AbsolutePath.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase);
    }
}
