using OAuth2.DTO;

namespace OAuth2.Services;

public interface ITokenIssuer
{
    ValueTask<TokenIssueResult> IssueAsync(string accountId, RawAccount rawAccount, string clientId, string scope, string? nonce, CancellationToken cancellationToken = default, long? authTime = null);
}
