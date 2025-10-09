using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Misc;
using OAuth2.Options;

namespace OAuth2.Services;

internal class RedisAccesses(IOptions<RedisOptions> options) : RedisConnection(options.Value), IAccesses
{
    public async ValueTask<Access> WriteAccessAsync(string id, TimeSpan expire, CancellationToken cancellationToken = default)
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
            new("account_id", id)
            ]).WaitAsync(cancellationToken);

        await tx.ExecuteAsync().WaitAsync(cancellationToken);

        return new Access
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };
    }

    public async ValueTask<bool> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();

        var accessKey = KeyNames.Access(accessToken);
        var refreshToken = await db.StringGetAsync(accessKey).WaitAsync(cancellationToken);
        if (refreshToken.IsNullOrEmpty)
        {
            return false;
        }

        var refreshKey = KeyNames.Refresh(refreshToken!);
        var savedAccessToken = await db.HashGetAsync(refreshKey, "access_token").WaitAsync(cancellationToken);
        return savedAccessToken == accessToken;
    }

    public async ValueTask<string?> RefreshAccessAsync(string refreshToken, TimeSpan expire, CancellationToken cancellationToken = default)
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
        var last = batch.HashSetAsync(refreshKey, "access_token", newAccessToken);

        batch.Execute();
        await last;

        return newAccessToken;
    }
}
