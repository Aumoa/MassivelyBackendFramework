using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Misc;
using OAuth2.Options;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAccesses(IOptions<RedisOptions> options) : RedisConnection(options.Value), IAccesses
{
    private static readonly RedisValue[] Fields =
    [
        "access_token",
        "account_id",
        "sub",
        "scope",
        "client_id"
    ];

    public async ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var tx = db.CreateTransaction();

        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var accessKey = KeyNames.Access(accessToken);
        _ = tx.StringSetAsync(accessKey, refreshToken, expire).WaitAsync(cancellationToken);

        var refreshKey = KeyNames.Refresh(refreshToken);
        _ = tx.HashSetAsync(refreshKey, [
            new("access_token", accessToken),
            new("account_id", id),
            new("sub", sub),
            new("scope", scope),
            new("client_id", clientId)
            ]).WaitAsync(cancellationToken);

        await tx.ExecuteAsync().WaitAsync(cancellationToken);

        return new Access
        {
            Id = id,
            Sub = sub,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Scope = scope,
            ClientId = clientId
        };
    }

    public async ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();

        var accessKey = KeyNames.Access(accessToken);
        var refreshToken = await db.StringGetAsync(accessKey).WaitAsync(cancellationToken);
        if (refreshToken.IsNullOrEmpty)
        {
            return null;
        }

        var refreshKey = KeyNames.Refresh(refreshToken!);
        var fields = await db.HashGetAsync(refreshKey, Fields).WaitAsync(cancellationToken);
        if (fields.Length != Fields.Length)
        {
            return null;
        }

        if (fields[0] != accessToken)
        {
            return null;
        }

        return new Access
        {
            Id = fields[1]!,
            Sub = fields[2]!,
            AccessToken = accessToken,
            RefreshToken = refreshToken!,
            Scope = fields[3]!,
            ClientId = fields[4]!
        };
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

        var newAccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var accessKey = KeyNames.Access(newAccessToken);

        _ = batch.StringSetAsync(accessKey, newRefreshToken, expire);
        var resultsTask = batch.HashGetAsync(refreshKey, ["account_id", "sub", "scope", "client_id"]).WaitAsync(cancellationToken);

        batch.Execute();
        var results = await resultsTask;

        var prevRefreshKey = refreshKey;
        refreshKey = KeyNames.Refresh(newRefreshToken);
        await db.HashSetAsync(refreshKey, [
            new("access_token", newAccessToken),
            new("account_id", results[0]!),
            new("sub", results[1]!),
            new("scope", results[2]!),
            new("client_id", results[3]!)
            ]).WaitAsync(cancellationToken);

        CleanupAsync();

        return new Access
        {
            Id = results[0]!,
            Sub = results[1]!,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            Scope = results[2]!,
            ClientId = results[3]!
        };

        async void CleanupAsync()
        {
            var batch = db.CreateBatch();

            _ = batch.KeyDeleteAsync(KeyNames.Access(accessToken!));
            var task = batch.KeyDeleteAsync(prevRefreshKey);

            batch.Execute();
            await task;
        }
    }
}
