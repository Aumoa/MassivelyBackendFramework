using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Misc;
using OAuth2.Options;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAccesses(IOptions<RedisOptions> options) : RedisConnection(options.Value), IAccesses
{
    public async ValueTask<Access> WriteAccessAsync(string id, string scope, string clientId, TimeSpan expire, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var tx = db.CreateTransaction();

        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var accessKey = KeyNames.Access(accessToken);
        _ = tx.StringSetAsync(accessKey, refreshToken, expire).WaitAsync(cancellationToken);

        var refreshKey = KeyNames.Refresh(refreshToken);
        _ = db.HashSetAsync(refreshKey, [
            new("access_token", accessToken),
            new("account_id", id),
            new("scope", scope),
            new("client_id", clientId)
            ]).WaitAsync(cancellationToken);

        await tx.ExecuteAsync().WaitAsync(cancellationToken);

        return new Access
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Scope = scope,
            ClientId = clientId
        };
    }

    private static readonly RedisValue[] VerifyFields =
    [
        "access_token",
        "account_id"
    ];

    public async ValueTask<string?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();

        var accessKey = KeyNames.Access(accessToken);
        var refreshToken = await db.StringGetAsync(accessKey).WaitAsync(cancellationToken);
        if (refreshToken.IsNullOrEmpty)
        {
            return null;
        }

        var refreshKey = KeyNames.Refresh(refreshToken!);
        var fields = await db.HashGetAsync(refreshKey, VerifyFields).WaitAsync(cancellationToken);
        if (fields.Length != VerifyFields.Length)
        {
            return null;
        }

        if (fields[0] != accessToken)
        {
            return null;
        }

        return fields[1];
    }

    public async ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var refreshKey = KeyNames.Refresh(refreshToken);
        var accessToken = await db.HashGetAsync(refreshKey, "access_token").WaitAsync(cancellationToken);
        if (accessToken.IsNullOrEmpty)
        {
            return null;
        }

        var batch = db.CreateBatch();

        var accessKey = KeyNames.Access(accessToken!);
        _ = batch.KeyDeleteAsync(accessKey).WaitAsync(cancellationToken);

        var newAccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        accessKey = KeyNames.Access(newAccessToken);
        _ = batch.StringSetAsync(accessKey, refreshToken, expire);
        _ = batch.HashSetAsync(refreshKey, "access_token", newAccessToken);
        var resultsTask = batch.HashGetAsync(refreshKey, ["scope", "client_id"]).WaitAsync(cancellationToken);

        batch.Execute();
        var results = await resultsTask;

        return new Access
        {
            AccessToken = accessToken!,
            RefreshToken = refreshToken,
            Scope = results[0]!,
            ClientId = results[1]!
        };
    }
}
