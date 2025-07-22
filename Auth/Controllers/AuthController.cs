using System.Net.Http.Headers;
using System.Text.Json.Nodes;
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

    public static ValueTask<IResult> Redirect(
        HttpContext context,
        [FromServices] IOptions<AuthOptions> options,
        [FromServices] IHttpClientFactory httpClientFactory,
        string provider,
        CancellationToken cancellationToken
        )
    {
        switch (provider)
        {
            case "google":
                return RedirectForGoogle(context, options, httpClientFactory, cancellationToken);
        }

        return ValueTask.FromResult(Results.BadRequest("Unexpected provider."));
    }

    private static async ValueTask<IResult> RedirectForGoogle(HttpContext context, IOptions<AuthOptions> options, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        if (context.Request.Query.TryGetValue("code", out var codeStringValues) == false)
        {
            return Results.BadRequest("Missing code parameter.");
        }

        var code = codeStringValues.ToString();

        using var httpClient = httpClientFactory.CreateClient("grant-for-google-auth");
        var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(
        [
            new("code", code),
            new("client_id", options.Value.ClientId),
            new("client_secret", options.Value.ClientSecret),
            new("redirect_uri", options.Value.RedirectUrl + "/api/auth/redirect/google"),
            new("grant_type", "authorization_code")
        ]), cancellationToken);

        var tokenContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        var tokenJson = JsonNode.Parse(tokenContent);
        if (tokenJson == null)
        {
            return Results.Unauthorized();
        }

        var accessToken = tokenJson["access_token"]?.GetValue<string>();
        if (accessToken == null)
        {
            return Results.Unauthorized();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userRes = await httpClient.SendAsync(request, cancellationToken);
        var userContent = await userRes.Content.ReadAsStringAsync(cancellationToken);
        var userJson = JsonNode.Parse(userContent);
        if (userJson == null)
        {
            return Results.Unauthorized();
        }

        string userId = userJson["id"]!.GetValue<string>()!;
        string email = userJson["email"]!.GetValue<string>()!;
        string name = userJson["name"]!.GetValue<string>()!;

        // TODO:
        return Results.Ok(new
        {
            userId,
            email,
            name
        });
    }

    public static IResult Login(IOptions<AuthOptions> options, string provider)
    {
        switch (provider)
        {
            case "google":
                string redirectUrl = options.Value.RedirectUrl + "/api/auth/redirect/google";
                string scope = "openid profile email";
                string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                    $"?response_type=code" +
                    $"&client_id={options.Value.ClientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUrl)}" +
                    $"&scope={Uri.EscapeDataString(scope)}";
                return Results.Redirect(authUrl);
        }

        return Results.BadRequest("Unexpected provider.");
    }
}
