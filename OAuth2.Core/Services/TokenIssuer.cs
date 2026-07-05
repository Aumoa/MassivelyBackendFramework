using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class TokenIssuer(IAccesses accesses, IAccountClaims accountClaims, IClientUserGroups groups, IJwt jwt, IOptions<HostOptions> hostOptions) : ITokenIssuer
{
    public async ValueTask<TokenIssueResult> IssueAsync(string accountId, RawAccount rawAccount, string clientId, string scope, string? nonce, CancellationToken cancellationToken = default, long? authTime = null, string? acr = null, string? userInfoClaims = null)
    {
        var sub = rawAccount.Sub;
        var access = await accesses.WriteAccessAsync(accountId, sub, scope, clientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken, authTime, userInfoClaims);
        var claims = await accountClaims.GetClaimsAsync(accountId, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(clientId, sub, cancellationToken);

        string? idToken = null;
        if (scope.Split(' ').Any(p => p is "openid" or "all"))
        {
            idToken = jwt.Issue(clientId, jwt.ConfigureClaims(rawAccount, scope, [.. claims, .. groupsClaim], nonce, true, authTime, acr));
        }

        var canReturnRefreshToken = clientId == hostOptions.Value.ClientId || ScopePolicy.HasOfflineAccess(scope);

        return new TokenIssueResult(new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)jwt.ExpiresIn.TotalSeconds,
            Scope = access.Scope,
            RefreshToken = canReturnRefreshToken ? access.RefreshToken : null,
            RefreshExpiresIn = canReturnRefreshToken ? (int)jwt.RefreshTokenExpiresIn.TotalSeconds : null,
            IdToken = idToken
        }, access);
    }
}
