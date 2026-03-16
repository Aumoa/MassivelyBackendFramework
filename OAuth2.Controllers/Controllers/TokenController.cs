using System.Security.Cryptography;
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
    /// Validates PKCE code_verifier against the stored code_challenge.
    /// </summary>
    private static bool ValidatePkce(string codeVerifier, string codeChallenge, string codeChallengeMethod)
    {
        if (codeChallengeMethod != "S256")
        {
            return false;
        }

        // RFC 7636 specifies ASCII encoding for the code_verifier before hashing
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computedChallenge = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return computedChallenge == codeChallenge;
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
                return BadRequest(new { error = "invalid_client", error_description = "client authentication failed" });
            }
        }
        else
        {
            var cclaims = await clientClaims.GetClaimsAsync(clientId, cancellationToken);
            if (cclaims.Length == 0)
            {
                logger.LogWarning("Client not found: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client", error_description = "client_id is unknown" });
            }

            var secret = cclaims.FirstOrDefault(p => p.Name == "secret").Value ?? string.Empty;
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogWarning("Client secret not configured: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client", error_description = "client secret is not configured" });
            }

            if (PasswordHasher.Verify(clientSecret, secret) == false)
            {
                logger.LogWarning("Client secret verification failed: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client", error_description = "client authentication failed" });
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

        // Verify and remove authorization code (one-time use)
        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            logger.LogWarning("Authorization code not found or already used");
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

        // Validate PKCE if code_challenge was present in the authorization request
        if (code.Value.CodeChallenge != null)
        {
            if (string.IsNullOrWhiteSpace(request.CodeVerifier))
            {
                logger.LogWarning("PKCE code_verifier missing for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "code_verifier_missing" });
            }

            var challengeMethod = code.Value.CodeChallengeMethod;
            if (string.IsNullOrEmpty(challengeMethod))
            {
                logger.LogWarning("Stored code_challenge_method is missing for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_code_verifier" });
            }

            if (!ValidatePkce(request.CodeVerifier, code.Value.CodeChallenge, challengeMethod))
            {
                logger.LogWarning("PKCE validation failed for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_code_verifier" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.CodeVerifier))
        {
            // code_verifier provided but no challenge was stored — reject to prevent downgrade attacks
            logger.LogWarning("code_verifier provided but no code_challenge was stored for client: {ClientId}", request.ClientId);
            return BadRequest(new { error = "invalid_code_verifier" });
        }

        // Validate client secret only when not using PKCE (PKCE serves as client authentication for public clients)
        if (code.Value.CodeChallenge == null)
        {
            if (string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                return BadRequest(new { error = "client_secret_missing" });
            }

            var validationError = await ValidateClientSecretAsync(request.ClientId, request.ClientSecret, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }
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
            return BadRequest(new { error = "invalid_request", error_description = "refresh_token is required" });
        }

        // Parse Basic authentication header
        TryParseBasicAuth(request);

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "client_id is required" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return BadRequest(new { error = "invalid_request", error_description = "client_secret is required" });
        }

        // Step 1: Query refresh token information without refreshing it yet
        // We need to verify the client ID before actually generating new tokens
        var oldAccess = await accesses.VerifyRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (!oldAccess.HasValue)
        {
            logger.LogWarning("Invalid or expired refresh token");
            return BadRequest(new { error = "invalid_grant", error_description = "refresh_token is invalid or expired" });
        }

        // Step 2: Verify Client ID match
        if (oldAccess.Value.ClientId != request.ClientId)
        {
            logger.LogWarning("Client ID mismatch in refresh token. Token ClientId: {TokenClientId}, Request ClientId: {RequestClientId}", 
                oldAccess.Value.ClientId, request.ClientId);
            return BadRequest(new { error = "invalid_grant", error_description = "client_id does not match the original token" });
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
            return BadRequest(new { error = "invalid_grant", error_description = "refresh_token is invalid or expired" });
        }

        var rawAccount = await accounts.GetRawAccountAsync(newAccess.Value.Id, cancellationToken);
        if (!rawAccount.HasValue)
        {
            logger.LogError("Account not found: {AccountId}", newAccess.Value.Id);
            return BadRequest(new { error = "invalid_grant", error_description = "associated account not found" });
        }

        var claims = await accountClaims.GetClaimsAsync(newAccess.Value.Id, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(request.ClientId, newAccess.Value.Sub, cancellationToken);

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

        logger.LogInformation("Token refreshed successfully for client: {ClientId}, account: {AccountId}", request.ClientId, newAccess.Value.Id);
        return Ok(response);
    }
}
