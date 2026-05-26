using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Localizations;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Controllers;

[ApiController]
public class AuthController(IOptions<HostOptions> options, ILogger<AuthController> logger) : ControllerBase
{
    private const string PkceVerifierCookieName = "oauth2_pkce_verifier";
    private const string PkceStateCookieName = "oauth2_pkce_state";

    private sealed record AuthorizeRequest(
        string? ResponseType,
        string? RedirectUri,
        string? ClientId,
        string? Scope,
        string? State,
        string? Nonce,
        string? Prompt,
        string? CodeChallenge,
        string? CodeChallengeMethod,
        string? MaxAge,
        string? RequestObject,
        string? RequestUri);

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
        [FromServices] IAuthorizationCodes authorizationCodes,
        [FromServices] IAccounts accounts,
        [FromServices] ITokenIssuer tokenIssuer,
        CancellationToken cancellationToken)
    {
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

            var authorizationCode = await authorizationCodes.PopAsync(code, cancellationToken);
            if (authorizationCode.HasValue == false)
            {
                return BadRequest("Authorization code is invalid or already used.");
            }

            if (authorizationCode.Value.ClientId != options.Value.ClientId ||
                authorizationCode.Value.RedirectUri != GetInternalRedirectUri())
            {
                return BadRequest("Authorization code is not valid for this client.");
            }

            if (!ValidatePkce(codeVerifier, authorizationCode.Value.CodeChallenge, authorizationCode.Value.CodeChallengeMethod))
            {
                return BadRequest("PKCE validation failed.");
            }

            if (!ScopePolicy.TryNormalize(authorizationCode.Value.Scope, true, out var normalizedScope, out _))
            {
                return BadRequest("Authorization code contains invalid scope.");
            }

            var rawAccount = await accounts.GetRawAccountAsync(authorizationCode.Value.AccountId, cancellationToken);
            if (!rawAccount.HasValue)
            {
                return Unauthorized();
            }

            var issueResult = await tokenIssuer.IssueAsync(
                authorizationCode.Value.AccountId,
                rawAccount.Value,
                authorizationCode.Value.ClientId,
                normalizedScope,
                authorizationCode.Value.Nonce,
                cancellationToken,
                authorizationCode.Value.AuthTime);
            var tokenResponse = issueResult.Response;
            var access = issueResult.Access;

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

            HttpContext.Response.Cookies.Append("id", access.Id, new CookieOptions
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
    public Task<IActionResult> AuthorizeGetAsync(
        [FromServices] IClients clients,
        [FromServices] IClientClaims clientClaims,
        [FromServices] IAccesses accesses,
        [FromServices] IJwt jwt,
        CancellationToken cancellationToken)
    {
        return AuthorizeAsync(ReadAuthorizeRequest(Request.Query), clients, clientClaims, accesses, jwt, cancellationToken);
    }

    [HttpPost("/authorize")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> AuthorizePostAsync(
        [FromServices] IClients clients,
        [FromServices] IClientClaims clientClaims,
        [FromServices] IAccesses accesses,
        [FromServices] IJwt jwt,
        CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        return await AuthorizeAsync(ReadAuthorizeRequest(form), clients, clientClaims, accesses, jwt, cancellationToken);
    }

    private async Task<IActionResult> AuthorizeAsync(
        AuthorizeRequest request,
        IClients clients,
        IClientClaims clientClaims,
        IAccesses accesses,
        IJwt jwt,
        CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "profile" : request.Scope;

        if (request.ResponseType != "code")
        {
            return BadRequest("Unsupported response type.");
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri) ||
            string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest("Missing required authorization request parameter.");
        }

        if (!TryParseMaxAge(request.MaxAge, out _))
        {
            return BadRequest("max_age must be a non-negative integer.");
        }

        string clientName;
        string normalizedScope;

        // hosting service
        if (request.ClientId == options.Value.ClientId)
        {
            if (!ScopePolicy.TryNormalize(scope, true, out normalizedScope, out var scopeError))
            {
                return BadRequest(scopeError);
            }

            if (request.RedirectUri == GetInternalRedirectUri())
            {
                clientName = "OAuth2";
            }
            else
            {
                return Error(Strings.ERRORS_INVALID_REDIRECT_URI);
            }

            if (!IsValidPkceChallenge(request.CodeChallenge, request.CodeChallengeMethod))
            {
                return BadRequest("PKCE S256 code challenge is required.");
            }
        }
        else
        {
            var targetClient = await clients.GetClientAsync(request.ClientId, cancellationToken);
            if (targetClient == null)
            {
                return Error(Strings.ERRORS_INVALID_CLIENT_ID);
            }

            var claims = await clientClaims.GetClaimsAsync(request.ClientId, cancellationToken);
            var allowedUris = claims.Where(p => p.Name == "redirect_uri");
            if (allowedUris.Any(p => p.Value == request.RedirectUri) == false)
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

            var hasPkce = HasPkceParameters(request.CodeChallenge, request.CodeChallengeMethod);
            var hasClientSecret = claims.Any(p => p.Name == "secret");
            if ((hasPkce && !IsValidPkceChallenge(request.CodeChallenge, request.CodeChallengeMethod)) ||
                (!hasPkce && !hasClientSecret))
            {
                return BadRequest("PKCE S256 code challenge is required.");
            }

            clientName = targetClient.Value.Name;
        }

        if (!string.IsNullOrWhiteSpace(request.RequestObject))
        {
            return OAuthError("request_not_supported");
        }

        if (!string.IsNullOrWhiteSpace(request.RequestUri))
        {
            return OAuthError("request_uri_not_supported");
        }

        if (HasPrompt(request.Prompt, "login"))
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
            return Redirect(QueryHelpers.AddQueryString("/login", new Dictionary<string, string?>
            {
                ["client_id"] = request.ClientId,
                ["redirect_uri"] = request.RedirectUri,
                ["response_type"] = request.ResponseType,
                ["scope"] = normalizedScope,
                ["state"] = request.State,
                ["nonce"] = request.Nonce,
                ["prompt"] = request.Prompt,
                ["code_challenge"] = request.CodeChallenge,
                ["code_challenge_method"] = request.CodeChallengeMethod,
                ["client_name"] = clientName,
                ["max_age"] = request.MaxAge
            }));
        }

        IActionResult OAuthError(string error)
        {
            return Redirect(QueryHelpers.AddQueryString(request.RedirectUri, new Dictionary<string, string?>
            {
                ["error"] = error,
                ["state"] = request.State
            }));
        }

        IActionResult Error(string message)
        {
            return Redirect(QueryHelpers.AddQueryString("/error", new Dictionary<string, string?>
            {
                ["error"] = message
            }));
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

            var access = await accesses.WriteAccessAsync(authorizationCode.Value.AccountId, rawAccount.Value.Sub, authorizationCode.Value.Scope, authorizationCode.Value.ClientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken, authorizationCode.Value.AuthTime);
            var claims = await accountClaims.GetClaimsAsync(authorizationCode.Value.AccountId, cancellationToken);
            var jwtToken = jwt.Issue(options.Value.ClientId, [
                new("access_token", access.AccessToken),
                new("refresh_token", access.RefreshToken),
                new("id", authorizationCode.Value.AccountId),
                .. jwt.ConfigureClaims(rawAccount.Value, authorizationCode.Value.Scope, claims, null, true, authorizationCode.Value.AuthTime)
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

    private static AuthorizeRequest ReadAuthorizeRequest(IQueryCollection values)
    {
        return new AuthorizeRequest(
            GetValue(values, "response_type"),
            GetValue(values, "redirect_uri"),
            GetValue(values, "client_id"),
            GetValue(values, "scope"),
            GetValue(values, "state"),
            GetValue(values, "nonce"),
            GetValue(values, "prompt"),
            GetValue(values, "code_challenge"),
            GetValue(values, "code_challenge_method"),
            GetValue(values, "max_age"),
            GetValue(values, "request"),
            GetValue(values, "request_uri"));
    }

    private static AuthorizeRequest ReadAuthorizeRequest(IFormCollection values)
    {
        return new AuthorizeRequest(
            GetValue(values, "response_type"),
            GetValue(values, "redirect_uri"),
            GetValue(values, "client_id"),
            GetValue(values, "scope"),
            GetValue(values, "state"),
            GetValue(values, "nonce"),
            GetValue(values, "prompt"),
            GetValue(values, "code_challenge"),
            GetValue(values, "code_challenge_method"),
            GetValue(values, "max_age"),
            GetValue(values, "request"),
            GetValue(values, "request_uri"));
    }

    private static string? GetValue(IQueryCollection values, string name)
    {
        var value = values[name].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? GetValue(IFormCollection values, string name)
    {
        var value = values[name].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool TryParseMaxAge(string? value, out long? maxAge)
    {
        maxAge = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            maxAge = parsed;
            return true;
        }

        return false;
    }

    private static bool HasPrompt(string? prompt, string value)
    {
        return prompt?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(value, StringComparer.Ordinal) == true;
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

    private static bool ValidatePkce(string codeVerifier, string? codeChallenge, string? codeChallengeMethod)
    {
        return IsValidPkceChallenge(codeChallenge, codeChallengeMethod) &&
               IsValidPkceParameter(codeVerifier) &&
               string.Equals(CreateCodeChallenge(codeVerifier), codeChallenge, StringComparison.Ordinal);
    }

    private static bool HasPkceParameters(string? codeChallenge, string? codeChallengeMethod)
    {
        return !string.IsNullOrWhiteSpace(codeChallenge) || !string.IsNullOrWhiteSpace(codeChallengeMethod);
    }

    private static bool IsValidPkceChallenge(string? codeChallenge, string? codeChallengeMethod)
    {
        return codeChallengeMethod == "S256" &&
               !string.IsNullOrWhiteSpace(codeChallenge) &&
               IsValidPkceParameter(codeChallenge);
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
