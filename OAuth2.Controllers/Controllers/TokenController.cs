using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/token")]
public class TokenController(IAuthorizationCodes authorizationCodes, IAccesses accesses, IJwt jwt, IAccounts accounts, IAccountClaims accountClaims, IClientClaims clientClaims, IClientUserGroups groups, IOptions<HostOptions> hostOptions, ILogger<TokenController> logger) : ControllerBase
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] TokenRequest request, CancellationToken cancellationToken)
    {
        switch (request.GrantType)
        {
            case "authorization_code":
                return await HandleAuthorizeCodeAsync(request, cancellationToken);
            case "refresh_token":
                return await HandleRefreshTokenAsync(request, cancellationToken);
            default:
                return BadRequest(new { error = "unsupported_grant_type" });
        }
    }

    private async ValueTask<IActionResult> HandleAuthorizeCodeAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "code_missing" });
        }

        // Basic 인증 헤더 파싱을 먼저 수행
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) == false && authHeader.StartsWith("Basic "))
        {
            try
            {
                var encoded = authHeader["Basic ".Length..];
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parts = decoded.Split(':');
                if (parts.Length == 2)
                {
                    var clientId = Uri.UnescapeDataString(parts[0]);
                    var clientSecret = Uri.UnescapeDataString(parts[1]);

                    request.ClientId = clientId;
                    request.ClientSecret = clientSecret;
                }
            }
            catch (Exception)
            {
                return BadRequest(new { error = "invalid_auth_header" });
            }
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "client_id_missing" });
        }

        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            return BadRequest(new { error = "code_not_exists" });
        }

        if (code.Value.RedirectUri != request.RedirectUri)
        {
            logger.LogInformation("Redirect URI mismatch. Expected: {Expected}, Actual: {Actual}", code.Value.RedirectUri, request.RedirectUri);
            return BadRequest(new { error = "redirect_uri_mismatch" });
        }

        if (request.ClientSecret == null)
        {
            return BadRequest(new { error = "invalid_client_secret" });
        }

        if (request.ClientId == hostOptions.Value.ClientId)
        {
            if (request.ClientSecret != hostOptions.Value.Secret)
            {
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }
        else
        {
            var cclaims = await clientClaims.GetClaimsAsync(code.Value.ClientId, cancellationToken);
            if (cclaims.Length == 0)
            {
                return BadRequest(new { error = "invalid_client_id" });
            }

            var secret = cclaims.FirstOrDefault(p => p.Name == "secret").Value ?? string.Empty;
            if (string.IsNullOrEmpty(secret))
            {
                return BadRequest(new { error = "invalid_client_id" });
            }

            if (PasswordHasher.Verify(request.ClientSecret, secret) == false)
            {
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }

        var rawAccount = await accounts.GetRawAccountAsync(code.Value.AccountId, cancellationToken);
        var access = await accesses.WriteAccessAsync(code.Value.AccountId, rawAccount.Value.Sub, code.Value.Scope, code.Value.ClientId, jwt.ExpiresIn, cancellationToken);
        var claims = await accountClaims.GetClaimsAsync(code.Value.AccountId, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(request.ClientId, rawAccount.Value.Sub, cancellationToken);

        string? idToken = null;
        if (code.Value.Scope.Split(' ').Any(p => p is "openid" or "all"))
        {
            idToken = jwt.Issue(code.Value.ClientId, jwt.ConfigureClaims(rawAccount.Value, code.Value.Scope, [.. claims, .. groupsClaim], code.Value.Nonce, true));
        }

        var response = new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)jwt.ExpiresIn.TotalSeconds,
            Scope = access.Scope,
            RefreshToken = access.RefreshToken,
            IdToken = idToken
        };

        return Ok(response);
    }

    private async ValueTask<IActionResult> HandleRefreshTokenAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (request.RefreshToken == null)
        {
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) == false && authHeader.StartsWith("Basic "))
        {
            try
            {
                var encoded = authHeader["Basic ".Length..];
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parts = decoded.Split(':');
                if (parts.Length == 2)
                {
                    var clientId = Uri.UnescapeDataString(parts[0]);
                    var clientSecret = Uri.UnescapeDataString(parts[1]);

                    request.ClientId = clientId;
                    request.ClientSecret = clientSecret;
                }
            }
            catch (Exception)
            {
                return BadRequest(new { error = "invalid_auth_header" });
            }
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "invalid_client_id" });
        }

        if (request.ClientSecret == null)
        {
            return BadRequest(new { error = "invalid_client_secret" });
        }

        var newAccess = await accesses.RefreshAccessAsync(request.RefreshToken, jwt.ExpiresIn, cancellationToken);
        if (newAccess.HasValue == false)
        {
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        if (newAccess.Value.ClientId != request.ClientId)
        {
            logger.LogWarning("Client ID mismatch in refresh token. Token ClientId: {TokenClientId}, Request ClientId: {RequestClientId}", 
                newAccess.Value.ClientId, request.ClientId);
            return BadRequest(new { error = "invalid_client" });
        }

        if (request.ClientId == hostOptions.Value.ClientId)
        {
            if (request.ClientSecret != hostOptions.Value.Secret)
            {
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }
        else
        {
            var cclaims = await clientClaims.GetClaimsAsync(request.ClientId, cancellationToken);
            if (cclaims.Length == 0)
            {
                return BadRequest(new { error = "invalid_client_id" });
            }

            var secret = cclaims.FirstOrDefault(p => p.Name == "secret").Value ?? string.Empty;
            if (string.IsNullOrEmpty(secret))
            {
                return BadRequest(new { error = "invalid_client_id" });
            }

            if (PasswordHasher.Verify(request.ClientSecret, secret) == false)
            {
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }

        var access = await accesses.VerifyAsync(newAccess.Value.AccessToken, cancellationToken);
        var rawAccount = await accounts.GetRawAccountAsync(access.Value.Id, cancellationToken);
        var claims = await accountClaims.GetClaimsAsync(access.Value.Id, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(request.ClientId, access.Value.Sub, cancellationToken);

        string? idToken = null;
        if (newAccess.Value.Scope.Split(' ').Any(p => p is "openid" or "all"))
        {
            idToken = jwt.Issue(newAccess.Value.ClientId, jwt.ConfigureClaims(rawAccount.Value, newAccess.Value.Scope, [.. claims, .. groupsClaim], null, true));
        }

        var response = new TokenResponse
        {
            AccessToken = newAccess.Value.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)jwt.ExpiresIn.TotalSeconds,
            Scope = newAccess.Value.Scope,
            RefreshToken = newAccess.Value.RefreshToken,
            IdToken = idToken
        };

        return Ok(response);
    }
}
