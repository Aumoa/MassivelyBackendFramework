using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

public class AuthorizedControllerBase(IAccesses accesses) : ControllerBase
{
    private const string ApiKeyPrefix = "mbf_";

    protected async ValueTask<IActionResult> VerifiedAsync(Func<Access, ValueTask<IActionResult>> body, string? accessToken, CancellationToken cancellationToken)
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

        Access? access;

        if (token.StartsWith(ApiKeyPrefix))
        {
            access = await ResolveApiKeyAccessAsync(token, cancellationToken);
        }
        else
        {
            access = await accesses.VerifyAsync(token, cancellationToken);
        }

        if (access.HasValue == false)
        {
            return Unauthorized("access_token is expired.");
        }

        return await body(access.Value);
    }

    private async ValueTask<Access?> ResolveApiKeyAccessAsync(string apiKey, CancellationToken cancellationToken)
    {
        var apiKeys = HttpContext.RequestServices.GetRequiredService<IApiKeys>();
        var apiKeyInfo = await apiKeys.VerifyApiKeyAsync(apiKey, cancellationToken);
        if (!apiKeyInfo.HasValue)
        {
            return null;
        }

        var accounts = HttpContext.RequestServices.GetRequiredService<IAccounts>();
        var rawAccount = await accounts.GetRawAccountAsync(apiKeyInfo.Value.AccountId, cancellationToken);
        if (!rawAccount.HasValue)
        {
            return null;
        }

        return new Access
        {
            Id = apiKeyInfo.Value.AccountId,
            Sub = rawAccount.Value.Sub,
            AccessToken = apiKey,
            RefreshToken = string.Empty,
            Scope = "all",
            ClientId = apiKeyInfo.Value.ClientId
        };
    }
}
