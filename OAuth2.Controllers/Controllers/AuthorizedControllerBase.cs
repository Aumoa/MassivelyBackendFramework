using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

public class AuthorizedControllerBase(IAccesses accesses) : ControllerBase
{
    protected async ValueTask<IActionResult> VerifiedAsync(Func<Access, ValueTask<IActionResult>> body, string? accessToken, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(body, accessToken, cancellationToken, requiredClientId: null);
    }

    protected async ValueTask<IActionResult> VerifiedAsync(Func<Access, ValueTask<IActionResult>> body, string? accessToken, CancellationToken cancellationToken, string? requiredClientId)
    {
        var token = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = accessToken ?? string.Empty;
        }

        if (token.StartsWith("Bearer "))
        {
            token = token["Bearer ".Length..];
        }

        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Authorization header is not included.");
        }

        var access = await accesses.VerifyAsync(token, cancellationToken);

        if (access.HasValue == false)
        {
            return Unauthorized("access_token is expired.");
        }

        if (!string.IsNullOrWhiteSpace(requiredClientId) &&
            !string.Equals(access.Value.ClientId, requiredClientId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        return await body(access.Value);
    }
}
