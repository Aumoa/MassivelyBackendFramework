using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace OpenIDConnect.Controllers;

[Route("auth")]
public sealed class OpenIdConnectLoginController(IAuthenticationStateProvider auth) : ControllerBase
{
    [HttpGet("oidc-login")]
    public IActionResult Login(
        [FromQuery(Name = "redirect_path")] string redirectPath,
        [FromQuery] string scope)
    {
        if (!TryCreateRedirectTarget(redirectPath, out var path, out var queryString))
        {
            return BadRequest("A local redirect_path is required.");
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return BadRequest("OAuth scope is required.");
        }

        var redirectUri = UriHelper.BuildAbsolute(
            HttpContext.Request.Scheme,
            HttpContext.Request.Host,
            HttpContext.Request.PathBase,
            path,
            queryString);

        return Redirect(auth.GenerateLoginUri(HttpContext, redirectUri, scope));
    }

    private static bool TryCreateRedirectTarget(
        string? redirectPath,
        out PathString path,
        out QueryString queryString)
    {
        path = PathString.Empty;
        queryString = QueryString.Empty;

        if (string.IsNullOrWhiteSpace(redirectPath))
        {
            return false;
        }

        var trimmed = redirectPath.Trim();
        if (trimmed.Contains("\\", StringComparison.Ordinal) ||
            trimmed.Contains("#", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return false;
        }

        var queryIndex = trimmed.IndexOf('?');
        var pathPart = queryIndex >= 0
            ? trimmed[..queryIndex]
            : trimmed;
        var queryPart = queryIndex >= 0
            ? trimmed[queryIndex..]
            : string.Empty;
        if (string.IsNullOrWhiteSpace(pathPart) || queryPart.Contains("#", StringComparison.Ordinal))
        {
            return false;
        }

        path = new PathString(pathPart.StartsWith("/", StringComparison.Ordinal)
            ? pathPart
            : "/" + pathPart);
        queryString = string.IsNullOrEmpty(queryPart)
            ? QueryString.Empty
            : new QueryString(queryPart);
        return true;
    }
}
