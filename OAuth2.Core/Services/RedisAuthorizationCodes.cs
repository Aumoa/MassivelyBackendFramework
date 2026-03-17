using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OAuth2.DTO;
using OAuth2.Misc;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAuthorizationCodes(RedisConnection multiplexer, ILogger<RedisAuthorizationCodes> logger) : IAuthorizationCodes
{
    public async ValueTask<string> PushAsync(AuthorizationCodeBody body, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var codeKey = KeyNames.AuthorizationCode(code);

        HashEntry[] entries = [
            new("account_id", body.AccountId),
            new("client_id", body.ClientId),
            new("scope", body.Scope),
            new("redirect_uri", body.RedirectUri),
            new("nonce", body.Nonce ?? string.Empty),
            new("code_challenge", body.CodeChallenge ?? string.Empty),
            new("code_challenge_method", body.CodeChallengeMethod ?? string.Empty)
        ];

        await db.HashSetAsync(codeKey, entries).WaitAsync(cancellationToken);
        await db.KeyExpireAsync(codeKey, TimeSpan.FromMinutes(5));
        return code;
    }

    public async ValueTask<AuthorizationCodeBody?> PopAsync(string code, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
        var codeKey = KeyNames.AuthorizationCode(code);
        var entries = await db.HashGetAllAsync(codeKey).WaitAsync(cancellationToken);
        if (entries.Length == 0)
        {
            return null;
        }

        await db.KeyDeleteAsync(codeKey).WaitAsync(cancellationToken);

        string? accountId = null;
        string? clientId = null;
        string? scope = null;
        string? redirectUri = null;
        string? nonce = null;  // nonce is optional
        string? codeChallenge = null;
        string? codeChallengeMethod = null;

        foreach (var entry in entries)
        {
            switch (entry.Name)
            {
                case "account_id":
                    accountId = entry.Value;
                    break;
                case "client_id":
                    clientId = entry.Value;
                    break;
                case "scope":
                    scope = entry.Value;
                    break;
                case "redirect_uri":
                    redirectUri = entry.Value;
                    break;
                case "nonce":
                    nonce = entry.Value;
                    break;
                case "code_challenge":
                    codeChallenge = entry.Value;
                    break;
                case "code_challenge_method":
                    codeChallengeMethod = entry.Value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(redirectUri))
        {
            logger.LogError("Invalid authorization code data: {Code}", code);
            return null;
        }

        return new AuthorizationCodeBody(
            accountId,
            clientId,
            scope,
            redirectUri,
            nonce,
            string.IsNullOrEmpty(codeChallenge) ? null : codeChallenge,
            string.IsNullOrEmpty(codeChallengeMethod) ? null : codeChallengeMethod
        );
    }
}
