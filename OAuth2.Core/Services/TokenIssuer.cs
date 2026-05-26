using OAuth2.DTO;

namespace OAuth2.Services;

internal class TokenIssuer(IAccesses accesses, IAccountClaims accountClaims, IClientUserGroups groups, IJwt jwt) : ITokenIssuer
{
    public async ValueTask<TokenIssueResult> IssueAsync(string accountId, RawAccount rawAccount, string clientId, string scope, string? nonce, CancellationToken cancellationToken = default, long? authTime = null)
    {
        var sub = rawAccount.Sub;
        var access = await accesses.WriteAccessAsync(accountId, sub, scope, clientId, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken, authTime);
        var claims = await accountClaims.GetClaimsAsync(accountId, cancellationToken);
        var groupsClaim = await groups.GetClientUserGroupsAsync(clientId, sub, cancellationToken);

        string? idToken = null;
        if (scope.Split(' ').Any(p => p is "openid" or "all"))
        {
            idToken = jwt.Issue(clientId, jwt.ConfigureClaims(rawAccount, scope, [.. claims, .. groupsClaim], nonce, true, authTime));
        }

        return new TokenIssueResult(new TokenResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)jwt.ExpiresIn.TotalSeconds,
            Scope = access.Scope,
            RefreshToken = access.RefreshToken,
            RefreshExpiresIn = (int)jwt.RefreshTokenExpiresIn.TotalSeconds,
            IdToken = idToken
        }, access);
    }
}
