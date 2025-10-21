using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/userinfo")]
public class UserInfoController(IAccesses accesses, IAccounts accounts, IAccountClaims claims) : AuthorizedControllerBase(accesses)
{
    [HttpGet]
    public async ValueTask<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async accountId =>
        {
            var sub = await accounts.GetSubAsync(accountId);
            var accountClaims = await claims.GetClaimsAsync(accountId, cancellationToken);
            return Ok(new UserInfo(
                sub,
                accountClaims.FirstOrDefault(p => p.Name == ClaimNames.Name).Value,
                accountClaims.FirstOrDefault(p => p.Name == ClaimNames.Email).Value,
                accountClaims.FirstOrDefault(p => p.Name == ClaimNames.Picture).Value
                ));
        }, cancellationToken);
    }
}
