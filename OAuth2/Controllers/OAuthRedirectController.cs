using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[Route("[controller]")]
public class RedirectController : Controller
{
    private readonly Options.HostOptions _hostOptions;
    private readonly HttpClient _http;
    private readonly IAccesses _access;

    public RedirectController(
        IOptions<Options.HostOptions> hostOptions,
        HttpClient http,
        IAccesses access)
    {
        _hostOptions = hostOptions.Value;
        _http = http;
        _access = access;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Redirect("/error");
        }

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = $"{Request.Scheme}://{Request.Host}/redirect",
            ["client_id"] = _hostOptions.ClientId,
            ["client_secret"] = _hostOptions.Secret
        };

        using var content = new FormUrlEncodedContent(formData);
        using var response = await _http.PostAsync($"{Request.Scheme}://{Request.Host}/api/v1/token", content);

        if (!response.IsSuccessStatusCode)
        {
            return Redirect("/error");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (tokenResponse == null)
        {
            return Redirect("/error");
        }

        var access = await _access.VerifyAsync(tokenResponse.AccessToken);
        if (!access.HasValue)
        {
            return Redirect("/error");
        }

        Response.Cookies.Append("access_token", tokenResponse.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        Response.Cookies.Append("refresh_token", tokenResponse.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        Response.Cookies.Append("id", access.Value.Id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        if (tokenResponse.IdToken != null)
        {
            Response.Cookies.Append("id_token", tokenResponse.IdToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });
        }

        return Redirect("/");
    }
}
