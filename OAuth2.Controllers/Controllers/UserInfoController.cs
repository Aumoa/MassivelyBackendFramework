using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/userinfo")]
public class UserInfoController(
    IAccesses accesses,
    IAccounts accounts,
    IAccountClaims claims,
    IJwt jwt,
    IClientUserGroups groups,
    IClientRoles roles,
    IAccountPictures accountPictures)
    : AuthorizedControllerBase(accesses)
{
    [HttpGet]
    public async ValueTask<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await GetUserInfoAsync(null, cancellationToken);
    }

    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm(Name = "access_token")] string? accessToken, CancellationToken cancellationToken)
    {
        return await GetUserInfoAsync(accessToken, cancellationToken);
    }

    private async ValueTask<IActionResult> GetUserInfoAsync(string? accessToken, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var rawAccount = await accounts.GetRawAccountAsync(access.Id, cancellationToken);
            if (!rawAccount.HasValue)
            {
                return Unauthorized("access_token account is invalid.");
            }

            AccountClaim[] accountClaims = [.. await claims.GetClaimsAsync(access.Id, cancellationToken), .. await groups.GetClientUserGroupsAsync(access.ClientId, access.Sub, cancellationToken), .. await roles.GetAccountRolesAsync(access.ClientId, access.Id, cancellationToken)];
            var legacyPictureUrl = GetLatestPictureClaimValue(accountClaims);
            await accountPictures.EnsurePictureAsync(access.Id, legacyPictureUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(legacyPictureUrl))
            {
                await claims.RemoveClaimsAsync(access.Id, JwtRegisteredClaimNames.Picture, cancellationToken);
            }

            var scopedClaims = jwt.ConfigureClaims(rawAccount.Value, access.Scope, accountClaims, null, false, additionalClaims: access.UserInfoClaims);
            return Ok(scopedClaims.ToDictionary(c => c.Type, c => GetClaimValue(c)));
        }, accessToken, cancellationToken);

        static object? GetClaimValue(Claim claim)
        {
            switch (claim.ValueType)
            {
                case ClaimValueTypes.Boolean:
                    return bool.Parse(claim.Value);
                case ClaimValueTypes.Integer64:
                    return long.Parse(claim.Value);
                default:
                    return claim.Type switch
                    {
                        JwtRegisteredClaimNames.Address => JsonSerializer.Deserialize<Dictionary<string, object>>(claim.Value),
                        "groups" => JsonSerializer.Deserialize<string[]>(claim.Value),
                        "roles" => JsonSerializer.Deserialize<string[]>(claim.Value),
                        _ => claim.Value,
                    };
            }
        }
    }

    private static string? GetLatestPictureClaimValue(AccountClaim[] claims)
    {
        return claims
            .Where(claim => claim.Name == JwtRegisteredClaimNames.Picture)
            .OrderByDescending(claim => claim.CreatedAt)
            .Select(claim => claim.Value)
            .FirstOrDefault();
    }
}
