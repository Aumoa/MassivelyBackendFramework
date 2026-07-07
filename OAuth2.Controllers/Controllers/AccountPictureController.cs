using Microsoft.AspNetCore.Mvc;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/account-picture")]
public sealed class AccountPictureController(IAccounts accounts, IAccountPictures accountPictures) : ControllerBase
{
    [HttpGet]
    public async ValueTask<IActionResult> GetAsync([FromQuery] string? sub, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sub))
        {
            return NotFound();
        }

        var accountId = await accounts.GetIdFromSubAsync(sub, cancellationToken);
        if (string.IsNullOrEmpty(accountId))
        {
            return NotFound();
        }

        var picture = await accountPictures.GetOrCreateDefaultPictureAsync(accountId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.ContentDisposition = "inline";

        return File(picture.Image, picture.ContentType);
    }
}
