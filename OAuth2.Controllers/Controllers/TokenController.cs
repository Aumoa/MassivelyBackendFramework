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

    /// <summary>
    /// Parses the Basic authentication header and extracts Client ID and Secret.
    /// </summary>
    private bool TryParseBasicAuth(TokenRequest request)
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
        {
            return false;
        }

        try
        {
            var encoded = authHeader["Basic ".Length..];
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            
            if (parts.Length == 2)
            {
                request.ClientId = Uri.UnescapeDataString(parts[0]);
                request.ClientSecret = Uri.UnescapeDataString(parts[1]);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Basic authentication header");
        }

        return false;
    }

    /// <summary>
    /// Validates the client secret.
    /// </summary>
    private async ValueTask<IActionResult?> ValidateClientSecretAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        if (clientId == hostOptions.Value.ClientId)
        {
            if (clientSecret != hostOptions.Value.Secret)
            {
                logger.LogWarning("Client secret validation failed for internal client: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }
        else
        {
            var cclaims = await clientClaims.GetClaimsAsync(clientId, cancellationToken);
            if (cclaims.Length == 0)
            {
                logger.LogWarning("Client not found: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client_id" });
            }

            var secret = cclaims.FirstOrDefault(p => p.Name == "secret").Value ?? string.Empty;
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogWarning("Client secret not configured: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client_id" });
            }

            if (PasswordHasher.Verify(clientSecret, secret) == false)
            {
                logger.LogWarning("Client secret verification failed: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client_secret" });
            }
        }

        return null;
    }

    private async ValueTask<IActionResult> HandleAuthorizeCodeAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "code_missing" });
        }

        // Parse Basic authentication header
        TryParseBasicAuth(request);

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "client_id_missing" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return BadRequest(new { error = "client_secret_missing" });
        }

        // Verify and remove authorization code (one-time use)
        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            logger.LogWarning("Authorization code not found or already used: {Code}", request.Code);
            return BadRequest(new { error = "code_not_exists" });
        }

        // Verify Client ID match
        if (code.Value.ClientId != request.ClientId)
        {
            logger.LogWarning("Client ID mismatch. Code ClientId: {CodeClientId}, Request ClientId: {RequestClientId}", 
                code.Value.ClientId, request.ClientId);
            return BadRequest(new { error = "invalid_client" });
        }

        // Verify Redirect URI
        if (code.Value.RedirectUri != request.RedirectUri)
        {
            logger.LogWarning("Redirect URI mismatch. Expected: {Expected}, Actual: {Actual}", 
                code.Value.RedirectUri, request.RedirectUri);
            return BadRequest(new { error = "redirect_uri_mismatch" });
        }

        // Validate client secret
        var validationError = await ValidateClientSecretAsync(request.ClientId, request.ClientSecret, cancellationToken);
        if (validationError != null)
        {
            return validationError;
        }

        // Issue tokens
        var rawAccount = await accounts.GetRawAccountAsync(code.Value.AccountId, cancellationToken);
        if (!rawAccount.HasValue)
        {
            logger.LogError("Account not found: {AccountId}", code.Value.AccountId);
            return BadRequest(new { error = "account_not_found" });
        }

        var access = await accesses.WriteAccessAsync(code.Value.AccountId, rawAccount.Value.Sub, code.Value.Scope, code.Value.ClientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken);
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

        logger.LogInformation("Token issued successfully for client: {ClientId}, account: {AccountId}", request.ClientId, code.Value.AccountId);
        return Ok(response);
    }

    private async ValueTask<IActionResult> HandleRefreshTokenAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "refresh_token_missing" });
        }

        // Parse Basic authentication header
        TryParseBasicAuth(request);

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "client_id_missing" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return BadRequest(new { error = "client_secret_missing" });
        }

        // Step 1: Query refresh token information without refreshing it yet
        // We need to verify the client ID before actually generating new tokens
        var oldAccess = await accesses.VerifyRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (!oldAccess.HasValue)
        {
            logger.LogWarning("Invalid or expired refresh token");
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        // Step 2: Verify Client ID match
        if (oldAccess.Value.ClientId != request.ClientId)
        {
            logger.LogWarning("Client ID mismatch in refresh token. Token ClientId: {TokenClientId}, Request ClientId: {RequestClientId}", 
                oldAccess.Value.ClientId, request.ClientId);
            return BadRequest(new { error = "invalid_client" });
        }

        // Step 3: Validate client secret
        var validationError = await ValidateClientSecretAsync(request.ClientId, request.ClientSecret, cancellationToken);
        if (validationError != null)
        {
            return validationError;
        }

        // Step 4: Generate new tokens after all validations pass
        var newAccess = await accesses.RefreshAccessAsync(request.RefreshToken, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken);
        if (newAccess.HasValue == false)
        {
            logger.LogWarning("Failed to refresh access token");
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        // Retrieve token information
        var access = await accesses.VerifyAsync(newAccess.Value.AccessToken, cancellationToken);
        if (!access.HasValue)
        {
            logger.LogError("Newly created access token verification failed");
            return StatusCode(500, new { error = "internal_server_error" });
        }

        var rawAccount = await accounts.GetRawAccountAsync(access.Value.Id, cancellationToken);
        if (!rawAccount.HasValue)
        {
            logger.LogError("Account not found: {AccountId}", access.Value.Id);
            return BadRequest(new { error = "account_not_found" });
        }

        var claims = await accountClaims.GetClaimsAsync(access.Value.Id, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(request.ClientId, access.Value.Sub, cancellationToken);

        // Issue IdToken only when openid scope is present
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

        logger.LogInformation("Token refreshed successfully for client: {ClientId}, account: {AccountId}", request.ClientId, access.Value.Id);
        return Ok(response);
    }
}
