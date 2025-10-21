using Microsoft.AspNetCore.Mvc;
using OAuth2.Services;

namespace OAuth2.Controllers;

public class AuthorizedControllerBase(IAccesses accesses) : ControllerBase
{
    protected async ValueTask<IActionResult> VerifiedAsync(Func<string, ValueTask<IActionResult>> body, CancellationToken cancellationToken)
    {
        var token = Request.Headers.Authorization.ToString();
        if (token.StartsWith("Bearer "))
        {
            token = token["Bearer ".Length..];
        }

        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Authorization header is not included.");
        }

        var ownerId = await accesses.VerifyAsync(token, cancellationToken);
        if (ownerId == null)
        {
            return Unauthorized("access_token is expired.");
        }

        return await body(ownerId);
    }
}
