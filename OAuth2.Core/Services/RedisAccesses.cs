using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Misc;
using OAuth2.Options;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAccesses(IOptions<RedisOptions> options, ILogger<RedisAccesses> logger) : RedisConnection(options.Value), IAccesses
{
    private static readonly RedisValue[] Fields =
    [
        "access_token",
        "account_id",
        "sub",
        "scope",
        "client_id"
    ];

    public async ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
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
        
        // Set TTL for refresh token
        _ = tx.KeyExpireAsync(refreshKey, refreshTokenExpire).WaitAsync(cancellationToken);

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

    public async ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var refreshKey = KeyNames.Refresh(refreshToken);
        
        logger.LogDebug("Verifying refresh token");
        
        // Check if key exists
        var exists = await db.KeyExistsAsync(refreshKey).WaitAsync(cancellationToken);
        if (!exists)
        {
            logger.LogWarning("Refresh token key does not exist in Redis");
            return null;
        }
        
        var fields = await db.HashGetAsync(refreshKey, Fields).WaitAsync(cancellationToken);
        
        logger.LogDebug("Retrieved {FieldCount} fields from Redis. Expected: {ExpectedCount}", 
            fields.Length, Fields.Length);
        
        if (fields.Length != Fields.Length)
        {
            logger.LogWarning("Field count mismatch. Expected: {Expected}, Actual: {Actual}", 
                Fields.Length, fields.Length);
            return null;
        }
        
        // Log each field value
        for (int i = 0; i < fields.Length; i++)
        {
            logger.LogDebug("Field[{Index}] ({Name}): {Value}", 
                i, Fields[i], fields[i].IsNullOrEmpty ? "<empty>" : "***");
        }
        
        if (fields[0].IsNullOrEmpty)
        {
            logger.LogWarning("Access token field is empty in refresh token data");
            return null;
        }

        logger.LogInformation("Refresh token verified successfully. ClientId: {ClientId}", fields[4]);
        
        return new Access
        {
            Id = fields[1]!,
            Sub = fields[2]!,
            AccessToken = fields[0]!,
            RefreshToken = refreshToken,
            Scope = fields[3]!,
            ClientId = fields[4]!
        };
    }

    public async ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
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
        
        // Set TTL for new refresh token
        await db.KeyExpireAsync(refreshKey, refreshTokenExpire).WaitAsync(cancellationToken);

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

    public async ValueTask RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();

        var accessKey = KeyNames.Access(accessToken);
        var refreshToken = await db.StringGetAsync(accessKey).WaitAsync(cancellationToken);

        var batch = db.CreateBatch();
        var accessDeleteTask = batch.KeyDeleteAsync(accessKey);
        Task? refreshDeleteTask = null;
        if (!refreshToken.IsNullOrEmpty)
        {
            refreshDeleteTask = batch.KeyDeleteAsync(KeyNames.Refresh(refreshToken!));
        }
        batch.Execute();

        await accessDeleteTask.WaitAsync(cancellationToken);
        if (refreshDeleteTask != null)
        {
            await refreshDeleteTask.WaitAsync(cancellationToken);
        }
    }
}
