using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.DTO;
using OAuth2.Localizations;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Controllers;

[ApiController]
public class AuthController(IOptions<HostOptions> options, HttpClient http, ILogger<AuthController> logger) : ControllerBase
{
    private const string PkceVerifierCookieName = "oauth2_pkce_verifier";
    private const string PkceStateCookieName = "oauth2_pkce_state";

    [HttpGet("/auth/login")]
    public IActionResult StartLogin()
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateCodeVerifier();

        var cookieOptions = CreatePkceCookieOptions();
        HttpContext.Response.Cookies.Append(PkceVerifierCookieName, verifier, cookieOptions);
        HttpContext.Response.Cookies.Append(PkceStateCookieName, state, cookieOptions);

        var authorizeUri = QueryHelpers.AddQueryString("/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = options.Value.ClientId,
            ["redirect_uri"] = GetInternalRedirectUri(),
            ["response_type"] = "code",
            ["scope"] = "all",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        });

        return Redirect(authorizeUri);
    }

    [HttpGet("/redirect")]
    public async Task<IActionResult> RedirectAsync(
        [FromQuery] string code,
        [FromQuery] string? state,
        [FromServices] IAccesses accesses,
        CancellationToken cancellationToken)
    {
        var baseUri = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}{HttpContext.Request.PathBase}/";
        var deleteCookieOptions = CreatePkceDeleteCookieOptions();

        try
        {
            if (!HttpContext.Request.Cookies.TryGetValue(PkceVerifierCookieName, out var codeVerifier) ||
                string.IsNullOrWhiteSpace(codeVerifier))
            {
                return BadRequest("PKCE code verifier is missing.");
            }

            if (!HttpContext.Request.Cookies.TryGetValue(PkceStateCookieName, out var expectedState) ||
                string.IsNullOrWhiteSpace(expectedState) ||
                !string.Equals(expectedState, state, StringComparison.Ordinal))
            {
                return BadRequest("OAuth state validation failed.");
            }

            var formData = new Dictionary<string, string>()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = GetInternalRedirectUri(),
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.Secret,
                ["code_verifier"] = codeVerifier
            };

            using var content = new FormUrlEncodedContent(formData);
            using var response = await http.PostAsync(baseUri + "api/v1/token", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Failed to parse token response.");

            var access = await accesses.VerifyAsync(tokenResponse.AccessToken, cancellationToken);
            if (access.HasValue == false)
            {
                return Unauthorized();
            }

            HttpContext.Response.Cookies.Append("access_token", tokenResponse.AccessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });

            HttpContext.Response.Cookies.Append("refresh_token", tokenResponse.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });

            HttpContext.Response.Cookies.Append("id", access.Value.Id, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });

            if (tokenResponse.IdToken != null)
            {
                HttpContext.Response.Cookies.Append("id_token", tokenResponse.IdToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict
                });
            }

            return Redirect("/");
        }
        finally
        {
            HttpContext.Response.Cookies.Delete(PkceVerifierCookieName, deleteCookieOptions);
            HttpContext.Response.Cookies.Delete(PkceStateCookieName, deleteCookieOptions);
        }
    }

    [HttpGet("/authorize")]
    public async Task<IActionResult> AuthorizeAsync(
        [FromQuery] string response_type,
        [FromQuery] string redirect_uri,
        [FromQuery] string client_id,
        [FromQuery] string? scope,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery] string? prompt,
        [FromQuery] string? code_challenge,
        [FromQuery] string? code_challenge_method,
        [FromServices] IClients clients,
        [FromServices] IClientClaims clientClaims,
        [FromServices] IAccesses accesses,
        [FromServices] IJwt jwt,
        CancellationToken cancellationToken)
    {
        scope ??= "profile";

        if (response_type != "code")
        {
            return BadRequest("Unsupported response type.");
        }

        if (string.IsNullOrWhiteSpace(code_challenge) ||
            code_challenge_method != "S256" ||
            !IsValidPkceParameter(code_challenge))
        {
            return BadRequest("PKCE S256 code challenge is required.");
        }

        string clientName;
        string normalizedScope;

        // hosting service
        if (client_id == options.Value.ClientId)
        {
            if (!ScopePolicy.TryNormalize(scope, true, out normalizedScope, out var scopeError))
            {
                return BadRequest(scopeError);
            }

            if (redirect_uri == GetInternalRedirectUri())
            {
                clientName = "OAuth2";
            }
            else
            {
                return Error(Strings.ERRORS_INVALID_REDIRECT_URI);
            }
        }
        else
        {
            var targetClient = await clients.GetClientAsync(client_id, cancellationToken);
            if (targetClient == null)
            {
                return Error(Strings.ERRORS_INVALID_CLIENT_ID);
            }

            var claims = await clientClaims.GetClaimsAsync(client_id, cancellationToken);
            var allowedUris = claims.Where(p => p.Name == "redirect_uri");
            if (allowedUris.Any(p => p.Value == redirect_uri) == false)
            {
                return Error(Strings.ERRORS_INVALID_REDIRECT_URI);
            }

            if (!ScopePolicy.TryNormalize(scope, false, out normalizedScope, out var scopeError))
            {
                return Error(scopeError ?? Strings.ERRORS_BAD_REQUEST);
            }

            var allowedScopes = claims.Where(p => p.Name == "scope").Select(p => p.Value);
            if (!ScopePolicy.IsAllowedByClient(normalizedScope, allowedScopes))
            {
                return Error("The requested scope is not allowed for this client.");
            }

            clientName = targetClient.Value.Name;
        }

        if (prompt == "login")
        {
            return Login();
        }

        const string CachedJwtPrefix = "cached_jwt_";
        foreach (var cookie in HttpContext.Request.Cookies)
        {
            if (!cookie.Key.StartsWith(CachedJwtPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var accountId = cookie.Key[CachedJwtPrefix.Length..];
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var validationParams = jwt.GetValidationParameters();
                var principal = handler.ValidateToken(cookie.Value, validationParams, out var validatedToken);
                var cachedJwt = (JwtSecurityToken)validatedToken;

                var access_token = cachedJwt.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
                if (access_token == null)
                {
                    DeleteCachedAccount(accountId);
                    continue;
                }

                var verified = await accesses.VerifyAsync(access_token);
                if (verified == null)
                {
                    var refresh_token = cachedJwt.Claims.FirstOrDefault(p => p.Type == "refresh_token")?.Value;
                    if (refresh_token == null)
                    {
                        DeleteCachedAccount(accountId);
                        continue;
                    }

                    var newAccess = await accesses.RefreshAccessAsync(refresh_token, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn);
                    if (newAccess.HasValue == false)
                    {
                        DeleteCachedAccount(accountId);
                        continue;
                    }

                    var except = cachedJwt.Claims.Where(p => p.Type is not ("access_token" or "refresh_token"));
                    var newCachedJwt = jwt.Issue(options.Value.ClientId, [.. except, new Claim("access_token", newAccess.Value.AccessToken), new Claim("refresh_token", newAccess.Value.RefreshToken)]);

                    HttpContext.Response.Cookies.Append($"cached_jwt_{accountId}", newCachedJwt, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddYears(10)
                    });

                    cachedJwt = handler.ReadJwtToken(newCachedJwt);
                }
            }
            catch (SecurityTokenException e)
            {
                logger.LogWarning("{Key} token validation failed: {Message}", cookie.Key, e.Message);
                DeleteCachedAccount(accountId);
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to process cached jwt token. {Message}", e.Message);
                DeleteCachedAccount(accountId);
            }

            void DeleteCachedAccount(string id)
            {
                HttpContext.Response.Cookies.Delete($"cached_jwt_{id}", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax
                });
            }
        }

        return Login();
        
        IActionResult Login()
        {
            return Redirect($"/login?client_id={EscapeDataString(client_id)}&redirect_uri={EscapeDataString(redirect_uri)}&response_type={EscapeDataString(response_type)}&scope={EscapeDataString(normalizedScope)}&state={EscapeDataString(state)}&nonce={EscapeDataString(nonce)}&prompt={EscapeDataString(prompt)}&code_challenge={EscapeDataString(code_challenge)}&code_challenge_method={EscapeDataString(code_challenge_method)}&client_name={EscapeDataString(clientName)}");
        }

        IActionResult Error(string message)
        {
            message = EscapeDataString(message);
            return Redirect($"/error?error={message}");
        }

        static string EscapeDataString(string? value)
        {
            if (value == null)
            {
                return "";
            }

            return Uri.EscapeDataString(value);
        }
    }

    [HttpGet("/authorize/int")]
    public async Task<IActionResult> AuthorizeIntAsync(
        [FromQuery] string redirect_uri,
        [FromQuery] string code,
        [FromServices] IAuthorizationCodes authorizationCodes,
        [FromServices] IAccounts accounts,
        [FromServices] IAccesses accesses,
        [FromServices] IAccountClaims accountClaims,
        [FromServices] IJwt jwt,
        CancellationToken cancellationToken)
    {
        var authorizationCode = await authorizationCodes.PopAsync(code);
        if (authorizationCode.HasValue == false)
        {
            return Error(Strings.ERRORS_INVALID_ACCESS);
        }

        // Validate the redirect_uri to prevent open redirect attacks.
        // Only allow http/https URIs or relative paths on the same origin.
        if (!IsValidRedirectUri(redirect_uri))
        {
            return Error(Strings.ERRORS_INVALID_REDIRECT_URI);
        }

        try
        {
            var rawAccount = await accounts.GetRawAccountAsync(authorizationCode.Value.AccountId, cancellationToken);
            if (!rawAccount.HasValue)
            {
                return Error(Strings.ERRORS_INVALID_ACCESS);
            }

            var access = await accesses.WriteAccessAsync(authorizationCode.Value.AccountId, rawAccount.Value.Sub, authorizationCode.Value.Scope, authorizationCode.Value.ClientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken);
            var claims = await accountClaims.GetClaimsAsync(authorizationCode.Value.AccountId, cancellationToken);
            var jwtToken = jwt.Issue(options.Value.ClientId, [
                new("access_token", access.AccessToken),
                new("refresh_token", access.RefreshToken),
                new("id", authorizationCode.Value.AccountId),
                .. jwt.ConfigureClaims(rawAccount.Value, authorizationCode.Value.Scope, claims, null, true)
            ]);

            HttpContext.Response.Cookies.Append($"cached_jwt_{authorizationCode.Value.AccountId}", jwtToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(10)
            });
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to cache access token: {Message}", e.Message);
        }

        return Redirect(redirect_uri);

        IActionResult Error(string message)
        {
            return Redirect($"/error?error={Uri.EscapeDataString(message)}");
        }

        static bool IsValidRedirectUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            // Allow safe relative paths on the same origin (must start with '/' and not '//')
            if (uri.StartsWith('/') && !uri.StartsWith("//"))
            {
                // Validate as a relative URI to block encoded slashes or other bypass attempts
                return Uri.TryCreate(uri, UriKind.Relative, out _);
            }

            // Allow only http and https absolute URIs
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            {
                return parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }
    }

    private string GetInternalRedirectUri()
    {
        return options.Value.Uri.TrimEnd('/') + "/redirect";
    }

    private static string CreateCodeVerifier()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreateCodeChallenge(string verifier)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static bool IsValidPkceParameter(string value)
    {
        return value.Length is >= 43 and <= 128 && value.All(static c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c is '-' or '.' or '_' or '~');
    }

    private static CookieOptions CreatePkceCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/redirect",
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        };
    }

    private static CookieOptions CreatePkceDeleteCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/redirect"
        };
    }
}
