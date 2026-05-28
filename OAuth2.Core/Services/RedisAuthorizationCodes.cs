using System.Security.Cryptography;
using System.Globalization;
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

        local consumed = redis.call('HGET', KEYS[1], 'consumed')
        if consumed == '1' then
            return nil
        end

        local values = redis.call('HMGET', KEYS[1],
            'account_id',
            'client_id',
            'scope',
            'redirect_uri',
            'nonce',
            'code_challenge',
            'code_challenge_method',
            'auth_time',
            'acr',
            'userinfo_claims')

        redis.call('HSET', KEYS[1], 'consumed', '1')
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
            new("code_challenge_method", body.CodeChallengeMethod ?? string.Empty),
            new("auth_time", body.AuthTime?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("acr", body.Acr ?? string.Empty),
            new("userinfo_claims", body.UserInfoClaims ?? string.Empty),
            new("consumed", "0")
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
        if (entries is not { Length: 10 })
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
        var authTimeValue = GetString(entries[7]);
        var acr = GetString(entries[8]);
        var userInfoClaims = GetString(entries[9]);

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
            string.IsNullOrEmpty(codeChallengeMethod) ? null : codeChallengeMethod,
            long.TryParse(authTimeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authTime) ? authTime : null,
            string.IsNullOrEmpty(acr) ? null : acr,
            string.IsNullOrEmpty(userInfoClaims) ? null : userInfoClaims
        );
    }

    public async ValueTask<string?> GetIssuedAccessTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
        var codeKey = KeyNames.AuthorizationCode(code);

        var fields = await db.HashGetAsync(codeKey, ["consumed", "issued_access_token"]).WaitAsync(cancellationToken);
        if (fields.Length != 2 || fields[0] != "1" || fields[1].IsNullOrEmpty)
        {
            return null;
        }

        return fields[1].ToString();
    }

    public async ValueTask StoreIssuedAccessTokenAsync(string code, string accessToken, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
        var codeKey = KeyNames.AuthorizationCode(code);

        await db.HashSetAsync(codeKey, "issued_access_token", accessToken).WaitAsync(cancellationToken);
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
