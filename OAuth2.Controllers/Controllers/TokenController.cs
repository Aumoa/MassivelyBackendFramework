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
public class TokenController(IAuthorizationCodes authorizationCodes, IAccesses accesses, IJwt jwt, IAccounts accounts, IAccountClaims accountClaims, IClientClaims clientClaims, IClientUserGroups groups, ITokenIssuer tokenIssuer, IOptions<HostOptions> hostOptions, IApiKeys apiKeys, ILogger<TokenController> logger) : ControllerBase
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] TokenRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        switch (request.GrantType)
        {
            case "authorization_code":
                return await HandleAuthorizeCodeAsync(request, cancellationToken);
            case "refresh_token":
                return await HandleRefreshTokenAsync(request, cancellationToken);
            case "api_key":
                return await HandleApiKeyAsync(request, cancellationToken);
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

        if (!IsValidPkceParameter(codeVerifier))
        {
            return false;
        }

        // RFC 7636 specifies ASCII encoding for the code_verifier before hashing
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computedChallenge = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return computedChallenge == codeChallenge;
    }

    private static bool IsValidPkceParameter(string value)
    {
        return value.Length is >= 43 and <= 128 && value.All(static c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c is '-' or '.' or '_' or '~');
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
            else
            {
                return null;
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

            string[] secrets = [.. cclaims.Where(p => p.Name == "secret").Select(c => c.Value)];
            if (secrets.Length == 0)
            {
                logger.LogWarning("Client secret not configured: {ClientId}", clientId);
                return BadRequest(new { error = "invalid_client", error_description = "client secret is not configured" });
            }

            foreach (var secret in secrets)
            {
                if (PasswordHasher.Verify(clientSecret, secret))
                {
                    return null;
                }
            }

            logger.LogWarning("Client secret verification failed: {ClientId}", clientId);
            return BadRequest(new { error = "invalid_client", error_description = "client authentication failed" });
        }
    }

    private async ValueTask<IActionResult?> ValidateAuthorizationCodeClientAsync(string clientId, string? clientSecret, CancellationToken cancellationToken)
    {
        if (clientId == hostOptions.Value.ClientId)
        {
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                return BadRequest(new { error = "invalid_client", error_description = "client_secret is required" });
            }

            return await ValidateClientSecretAsync(clientId, clientSecret, cancellationToken);
        }

        var cclaims = await clientClaims.GetClaimsAsync(clientId, cancellationToken);
        string[] secrets = [.. cclaims.Where(p => p.Name == "secret").Select(c => c.Value)];
        if (secrets.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return BadRequest(new { error = "invalid_client", error_description = "client_secret is required" });
        }

        foreach (var secret in secrets)
        {
            if (PasswordHasher.Verify(clientSecret, secret))
            {
                return null;
            }
        }

        logger.LogWarning("Client secret verification failed: {ClientId}", clientId);
        return BadRequest(new { error = "invalid_client", error_description = "client authentication failed" });
    }

    private async ValueTask<bool> HasClientSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        if (clientId == hostOptions.Value.ClientId)
        {
            return !string.IsNullOrWhiteSpace(hostOptions.Value.Secret);
        }

        var cclaims = await clientClaims.GetClaimsAsync(clientId, cancellationToken);
        return cclaims.Any(p => p.Name == "secret");
    }

    private async ValueTask<IActionResult> HandleAuthorizeCodeAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "invalid_request", error_description = "code is required" });
        }

        // Parse Basic authentication header
        TryParseBasicAuth(request);

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "client_id is required" });
        }

        // Verify and remove authorization code (one-time use)
        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            logger.LogWarning("Authorization code not found or already used");
            var issuedAccessToken = await authorizationCodes.GetIssuedAccessTokenAsync(request.Code, cancellationToken);
            if (!string.IsNullOrWhiteSpace(issuedAccessToken))
            {
                await accesses.RevokeAsync(issuedAccessToken, cancellationToken);
            }

            return BadRequest(new { error = "invalid_grant", error_description = "authorization code is invalid or already used" });
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
            return BadRequest(new { error = "invalid_grant", error_description = "redirect_uri does not match authorization code" });
        }

        var validationError = await ValidateAuthorizationCodeClientAsync(request.ClientId, request.ClientSecret, cancellationToken);
        if (validationError != null)
        {
            return validationError;
        }

        if (code.Value.CodeChallenge == null)
        {
            if (!await HasClientSecretAsync(request.ClientId, cancellationToken))
            {
                logger.LogWarning("Authorization code was issued without a PKCE challenge for public client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_grant", error_description = "PKCE is required for public clients" });
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.CodeVerifier))
            {
                logger.LogWarning("PKCE code_verifier missing for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_request", error_description = "code_verifier is required" });
            }

            var challengeMethod = code.Value.CodeChallengeMethod;
            if (string.IsNullOrEmpty(challengeMethod))
            {
                logger.LogWarning("Stored code_challenge_method is missing for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_grant", error_description = "code_verifier is invalid" });
            }

            if (!ValidatePkce(request.CodeVerifier, code.Value.CodeChallenge, challengeMethod))
            {
                logger.LogWarning("PKCE validation failed for client: {ClientId}", request.ClientId);
                return BadRequest(new { error = "invalid_grant", error_description = "code_verifier is invalid" });
            }
        }

        if (!ScopePolicy.TryNormalize(code.Value.Scope, code.Value.ClientId == hostOptions.Value.ClientId, out var normalizedScope, out _))
        {
            logger.LogWarning("Authorization code contains invalid scope for client: {ClientId}", code.Value.ClientId);
            return BadRequest(new { error = "invalid_scope" });
        }

        // Issue tokens
        var rawAccount = await accounts.GetRawAccountAsync(code.Value.AccountId, cancellationToken);
        if (!rawAccount.HasValue)
        {
            logger.LogError("Account not found: {AccountId}", code.Value.AccountId);
            return BadRequest(new { error = "invalid_grant", error_description = "authorization code account is invalid" });
        }

        var tokenResponse = await GenerateTokenResponseAsync(code.Value.AccountId, rawAccount.Value, code.Value.ClientId, normalizedScope, code.Value.Nonce, code.Value.AuthTime, code.Value.Acr, code.Value.UserInfoClaims, cancellationToken);
        await authorizationCodes.StoreIssuedAccessTokenAsync(request.Code, tokenResponse.AccessToken, cancellationToken);

        logger.LogInformation("Token issued successfully for client: {ClientId}, account: {AccountId}", request.ClientId, code.Value.AccountId);
        return Ok(tokenResponse);
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

        var validationError = await ValidateClientSecretAsync(request.ClientId, request.ClientSecret, cancellationToken);
        if (validationError != null)
        {
            return validationError;
        }

        var newAccess = await accesses.RefreshAccessAsync(request.RefreshToken, request.ClientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken);
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
            idToken = jwt.Issue(newAccess.Value.ClientId, jwt.ConfigureClaims(rawAccount.Value, newAccess.Value.Scope, [.. claims, .. groupsClaim], null, true, newAccess.Value.AuthTime));
        }

        var response = new TokenResponse
        {
            AccessToken = newAccess.Value.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)jwt.ExpiresIn.TotalSeconds,
            Scope = newAccess.Value.Scope,
            RefreshToken = newAccess.Value.RefreshToken,
            RefreshExpiresIn = (int)jwt.RefreshTokenExpiresIn.TotalSeconds,
            IdToken = idToken
        };

        logger.LogInformation("Token refreshed successfully for client: {ClientId}, account: {AccountId}", request.ClientId, newAccess.Value.Id);
        return Ok(response);
    }

    private async ValueTask<IActionResult> HandleApiKeyAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "invalid_request", error_description = "code is required" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "client_id is required" });
        }

        var apiKey = request.Code;
        var apiKeyInfo = await apiKeys.VerifyApiKeyAsync(apiKey, cancellationToken);
        if (!apiKeyInfo.HasValue)
        {
            return BadRequest(new { error = "invalid_grant", error_description = "api_key is invalid or does not exist" });
        }

        // Validate client restriction: if the key is restricted to a specific client, enforce it
        if (apiKeyInfo.Value.AllowedClientId != null && apiKeyInfo.Value.AllowedClientId != request.ClientId)
        {
            logger.LogWarning("API key client restriction violated. Allowed: {Allowed}, Requested: {Requested}", apiKeyInfo.Value.AllowedClientId, request.ClientId);
            return BadRequest(new { error = "invalid_grant", error_description = "api_key is not allowed for this client" });
        }

        var accountId = apiKeyInfo.Value.AccountId;

        var rawAccount = await accounts.GetRawAccountAsync(accountId, cancellationToken);
        if (!rawAccount.HasValue)
        {
            return BadRequest(new { error = "invalid_grant", error_description = "associated account not found" });
        }

        // Use the scope defined on the API key; fall back to "all" if unrestricted
        var scope = apiKeyInfo.Value.AllowedScope ?? "all";
        if (!ScopePolicy.TryNormalize(scope, true, out var normalizedScope, out _))
        {
            logger.LogWarning("API key contains invalid scope configuration. ApiKeyId: {ApiKeyId}", apiKeyInfo.Value.Id);
            return BadRequest(new { error = "invalid_grant", error_description = "api_key scope is invalid" });
        }

        var tokenResponse = await GenerateTokenResponseAsync(accountId, rawAccount.Value, request.ClientId, normalizedScope, null, null, null, null, cancellationToken);

        logger.LogInformation("Token issued successfully for client: {ClientId}, account: {AccountId} using API key.", request.ClientId, accountId);
        return Ok(tokenResponse);
    }

    private async ValueTask<TokenResponse> GenerateTokenResponseAsync(string accountId, RawAccount rawAccount, string clientId, string scope, string? nonce, long? authTime, string? acr, string? userInfoClaims, CancellationToken cancellationToken)
    {
        var issueResult = await tokenIssuer.IssueAsync(accountId, rawAccount, clientId, scope, nonce, cancellationToken, authTime, acr, userInfoClaims);
        return issueResult.Response;
    }
}
