using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OAuth2.DTO;
using OAuth2.Misc;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAuthorizationCodes(RedisConnection multiplexer, ILogger<RedisAuthorizationCodes> logger) : IAuthorizationCodes
{
    private const string PopScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then
            return nil
        end

        local values = redis.call('HMGET', KEYS[1],
            'account_id',
            'client_id',
            'scope',
            'redirect_uri',
            'nonce',
            'code_challenge',
            'code_challenge_method')

        redis.call('DEL', KEYS[1])
        return values
        """;

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
        var result = await db.ScriptEvaluateAsync(PopScript, [codeKey]).WaitAsync(cancellationToken);
        if (result.IsNull)
        {
            return null;
        }

        var entries = (RedisResult[]?)result;
        if (entries is not { Length: 7 })
        {
            logger.LogError("Invalid authorization code data: {Code}", code);
            return null;
        }

        var accountId = GetString(entries[0]);
        var clientId = GetString(entries[1]);
        var scope = GetString(entries[2]);
        var redirectUri = GetString(entries[3]);
        var nonce = GetString(entries[4]);  // nonce is optional
        var codeChallenge = GetString(entries[5]);
        var codeChallengeMethod = GetString(entries[6]);

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

    private static string? GetString(RedisResult result)
    {
        if (result.IsNull)
        {
            return null;
        }

        return ((RedisValue)result).ToString();
    }
}
