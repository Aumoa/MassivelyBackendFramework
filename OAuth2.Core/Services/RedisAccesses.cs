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

        // Read the current generation for this sub so the token can be invalidated later
        var genValue = await db.StringGetAsync(KeyNames.UserGen(sub)).WaitAsync(cancellationToken);
        var gen = genValue.IsNullOrEmpty ? 0L : (long)genValue;

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
            new("client_id", clientId),
            new("gen", gen)
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

        // Check the token generation against the user's current generation to detect
        // whether all tokens were invalidated (e.g. after a password change).
        var sub = fields[2]!.ToString();
        var batch = db.CreateBatch();
        var storedGenTask = batch.HashGetAsync(refreshKey, "gen");
        var currentGenTask = batch.StringGetAsync(KeyNames.UserGen(sub));
        batch.Execute();

        var storedGenValue = await storedGenTask.WaitAsync(cancellationToken);
        var currentGenValue = await currentGenTask.WaitAsync(cancellationToken);

        var storedGen = storedGenValue.IsNullOrEmpty ? 0L : (long)storedGenValue;
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        if (storedGen != currentGen)
        {
            return null;
        }

        return new Access
        {
            Id = fields[1]!,
            Sub = sub,
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

        // Check the token generation against the user's current generation
        var sub = fields[2]!.ToString();
        var storedGenValue = await db.HashGetAsync(refreshKey, "gen").WaitAsync(cancellationToken);
        var currentGenValue = await db.StringGetAsync(KeyNames.UserGen(sub)).WaitAsync(cancellationToken);
        var storedGen = storedGenValue.IsNullOrEmpty ? 0L : (long)storedGenValue;
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        if (storedGen != currentGen)
        {
            logger.LogWarning("Refresh token generation mismatch. Token has been invalidated.");
            return null;
        }

        logger.LogInformation("Refresh token verified successfully. ClientId: {ClientId}", fields[4]);
        
        return new Access
        {
            Id = fields[1]!,
            Sub = sub,
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

        // Read all needed fields in one call, including gen
        var refreshFields = await db.HashGetAsync(refreshKey,
            ["access_token", "gen", "account_id", "sub", "scope", "client_id"])
            .WaitAsync(cancellationToken);

        var accessToken = refreshFields[0];
        if (accessToken.IsNullOrEmpty)
        {
            return null;
        }

        // Validate generation: reject tokens that belong to an invalidated session
        var sub = refreshFields[3]!.ToString();
        var storedGen = refreshFields[1].IsNullOrEmpty ? 0L : (long)refreshFields[1];
        var currentGenValue = await db.StringGetAsync(KeyNames.UserGen(sub)).WaitAsync(cancellationToken);
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        if (storedGen != currentGen)
        {
            return null;
        }

        var batch = db.CreateBatch();

        var newAccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var accessKey = KeyNames.Access(newAccessToken);

        _ = batch.StringSetAsync(accessKey, newRefreshToken, expire);
        batch.Execute();

        var prevRefreshKey = refreshKey;
        refreshKey = KeyNames.Refresh(newRefreshToken);
        await db.HashSetAsync(refreshKey, [
            new("access_token", newAccessToken),
            new("account_id", refreshFields[2]!),
            new("sub", sub),
            new("scope", refreshFields[4]!),
            new("client_id", refreshFields[5]!),
            new("gen", storedGen)
            ]).WaitAsync(cancellationToken);
        
        // Set TTL for new refresh token
        await db.KeyExpireAsync(refreshKey, refreshTokenExpire).WaitAsync(cancellationToken);

        CleanupAsync();

        return new Access
        {
            Id = refreshFields[2]!,
            Sub = sub,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            Scope = refreshFields[4]!,
            ClientId = refreshFields[5]!
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

    public async ValueTask InvalidateAllTokensAsync(string sub, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var userGenKey = KeyNames.UserGen(sub);

        // Increment the generation counter. Any existing token (access or refresh) that
        // was issued with a lower generation value will be rejected by Verify/Refresh.
        await db.StringIncrementAsync(userGenKey).WaitAsync(cancellationToken);

        // Keep the key alive long enough to outlast any existing long-lived refresh tokens.
        await db.KeyExpireAsync(userGenKey, TimeSpan.FromDays(90)).WaitAsync(cancellationToken);
    }
}
