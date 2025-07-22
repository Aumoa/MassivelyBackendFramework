using Auth.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth.Controllers;

internal static class AuthController
{
    public static IResult Status()
    {
        return Results.Json(new
        {
            status = "ok"
        });
    }

    public static IResult Redirect(string provider, [FromQuery] string state)
    {
        switch (provider)
        {
            case "google":
                return Results.Redirect(state);
        }

        return Results.BadRequest();
    }

    public static IResult Login(HttpRequest request, IOptions<AuthOptions> options, string provider)
    {
        switch (provider)
        {
            case "google":
                string redirectUrl = options.Value.RedirectUrl + "/api/auth/redirect/google";
                string scope = "openid profile email";
                string returnUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";
                string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                    $"?response_type=code" +
                    $"&client_id={options.Value.ClientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUrl)}" +
                    $"&scope={Uri.EscapeDataString(scope)}" +
                    $"&state={Uri.EscapeDataString(returnUrl)}";
                return Results.Redirect(authUrl);
            default:
                return Results.BadRequest();
        }
    }
}
