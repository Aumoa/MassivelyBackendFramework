using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/token")]
public class TokenController(IAuthorizationCodes authorizationCodes, IAccesses accesses, IJwt jwt, IAccounts accounts, IAccountClaims accountClaims, IClientClaims clientClaims, IOptions<HostOptions> hostOptions) : ControllerBase
{
    private static readonly TimeSpan ExpiresIn = TimeSpan.FromHours(1);

    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] TokenRequest request, CancellationToken cancellationToken)
    {
        if (request.GrantType != "authorization_code")
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        var code = await authorizationCodes.PopAsync(request.Code, cancellationToken);
        if (code.HasValue == false)
        {
            return BadRequest(new { error = "invalid_grant" });
        }

        if (code.Value.RedirectUri == request.RedirectUri == false)
        {
            return BadRequest(new { error = "invalid_grant" });
        }

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) == false)
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

                    request = request with { ClientId = clientId, ClientSecret = clientSecret }; string.IsNullOrEmpty(clientSecret);

                    request.ClientId = clientId;
                    request.ClientSecret = clientSecret;
                }
            }
            catch (Exception)
            {
                return BadRequest("Failed to decode Basic auth header.");
            }
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

        var access = await accesses.WriteAccessAsync(code.Value.AccountId, code.Value.Scope, code.Value.ClientId, ExpiresIn, cancellationToken);
        var rawAccount = await accounts.GetRawAccountAsync(code.Value.AccountId, cancellationToken);
        var claims = await accountClaims.GetClaimsAsync(code.Value.AccountId, cancellationToken);

        var idTokenClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, rawAccount.Value.Sub),
            new(JwtRegisteredClaimNames.Name, rawAccount.Value.Name),
            new(JwtRegisteredClaimNames.Email, rawAccount.Value.Email),
            new(JwtRegisteredClaimNames.Picture, claims.FirstOrDefault(p => p.Name == JwtRegisteredClaimNames.Picture).Value ?? string.Empty),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.Add(ExpiresIn).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrEmpty(code.Value.Nonce))
        {
            idTokenClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, code.Value.Nonce));
        }

        var response = new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)ExpiresIn.TotalSeconds,
            Scope = access.Scope,
            RefreshToken = access.RefreshToken,
            IdToken = jwt.Issue(code.Value.ClientId, [.. idTokenClaims])
        };

        return Ok(response);
    }

    [HttpPost("refresh")]
    public async ValueTask<IActionResult> RefreshAsync([FromForm] TokenRefreshRequest request, CancellationToken cancellationToken)
    {
        var newAccess = await accesses.RefreshAccessAsync(request.RefreshToken, ExpiresIn, cancellationToken);
        if (newAccess.HasValue == false)
        {
            return BadRequest(new { error = "invalid_refresh_token" });
        }

        var response = new TokenResponse
        {
            AccessToken = newAccess.Value.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)ExpiresIn.TotalSeconds,
            Scope = newAccess.Value.Scope,
            RefreshToken = newAccess.Value.RefreshToken,
            IdToken = jwt.Issue(newAccess.Value.ClientId)
        };

        return Ok(response);
    }
}
