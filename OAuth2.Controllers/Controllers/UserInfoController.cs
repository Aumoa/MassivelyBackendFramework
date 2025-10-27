using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/userinfo")]
public class UserInfoController(IAccesses accesses, IAccounts accounts, IAccountClaims claims, IJwt jwt) : AuthorizedControllerBase(accesses)
{
    [HttpGet]
    [HttpPost]
    public async ValueTask<IActionResult> GetAsync([FromForm] UserInfoRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var rawAccount = (await accounts.GetRawAccountAsync(access.Id)).Value!;
            var accountClaims = await claims.GetClaimsAsync(access.Id, cancellationToken);
            var scopedClaims = jwt.ConfigureClaims(rawAccount, access.Scope, accountClaims, null);
            return Ok(scopedClaims.ToDictionary(c => c.Type, c => c.Value));
        }, request.AccessToken, cancellationToken);
    }
}
