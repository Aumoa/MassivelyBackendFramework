using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/userinfo")]
public class UserInfoController(IAccesses accesses, IAccounts accounts, IAccountClaims claims) : AuthorizedControllerBase(accesses)
{
    [HttpGet]
    [HttpPost]
    public async ValueTask<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async accountId =>
        {
            var rawAccount = (await accounts.GetRawAccountAsync(accountId)).Value!;
            var accountClaims = await claims.GetClaimsAsync(accountId, cancellationToken);
            return Ok(new UserInfo(
                rawAccount.Sub,
                rawAccount.Name,
                rawAccount.Email,
                accountClaims.FirstOrDefault(p => p.Name == JwtRegisteredClaimNames.Picture).Value
                ));
        }, cancellationToken);
    }
}
