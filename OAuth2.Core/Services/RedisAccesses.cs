using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OAuth2.DTO;
using OAuth2.Misc;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisAccesses(RedisConnection multiplexer, ILogger<RedisAccesses> logger) : IAccesses
{
    private static readonly TimeSpan RefreshReplayWindow = TimeSpan.FromSeconds(30);

    private const string RotateRefreshTokenScript = """
        local accessToken = redis.call('HGET', KEYS[1], 'access_token')
        if accessToken == false or accessToken == '' then
            return nil
        end

        local stableRefresh = redis.call('HGET', KEYS[1], 'stable_refresh')
        if stableRefresh == false or stableRefresh == '' then
            stableRefresh = '0'
        end

        if stableRefresh ~= '1' and accessToken ~= ARGV[6] then
            return nil
        end

        local accountId = redis.call('HGET', KEYS[1], 'account_id')
        local sub = redis.call('HGET', KEYS[1], 'sub')
        local scope = redis.call('HGET', KEYS[1], 'scope')
        local clientId = redis.call('HGET', KEYS[1], 'client_id')

        if accountId == false or sub == false or scope == false or clientId == false then
            return nil
        end

        if ARGV[7] ~= '' and clientId ~= ARGV[7] then
            return nil
        end

        if sub ~= ARGV[5] then
            return nil
        end

        local storedGen = redis.call('HGET', KEYS[1], 'gen')
        if storedGen == false or storedGen == '' then
            storedGen = '0'
        end

        local storedClientGen = redis.call('HGET', KEYS[1], 'client_gen')
        if storedClientGen == false or storedClientGen == '' then
            storedClientGen = '0'
        end

        local currentGen = redis.call('GET', KEYS[4])
        if currentGen == false or currentGen == '' then
            currentGen = '0'
        end

        local currentClientGen = redis.call('GET', KEYS[7])
        if currentClientGen == false or currentClientGen == '' then
            currentClientGen = '0'
        end

        if storedGen ~= currentGen or storedClientGen ~= currentClientGen then
            return nil
        end

        if stableRefresh == '1' then
            redis.call('SET', KEYS[2], ARGV[9], 'PX', ARGV[3])
            redis.call('HSET', KEYS[1], 'access_token', ARGV[2])
            redis.call('PEXPIRE', KEYS[1], ARGV[4])

            return { accountId, sub, scope, clientId, ARGV[9] }
        end

        redis.call('SET', KEYS[2], ARGV[1], 'PX', ARGV[3])
        redis.call('HSET', KEYS[3],
            'access_token', ARGV[2],
            'account_id', accountId,
            'sub', sub,
            'scope', scope,
            'client_id', clientId,
            'gen', storedGen,
            'client_gen', storedClientGen,
            'stable_refresh', stableRefresh)
        redis.call('PEXPIRE', KEYS[3], ARGV[4])
        redis.call('HSET', KEYS[6],
            'access_token', ARGV[2],
            'refresh_token', ARGV[1],
            'account_id', accountId,
            'sub', sub,
            'scope', scope,
            'client_id', clientId,
            'gen', storedGen,
            'client_gen', storedClientGen)
        redis.call('PEXPIRE', KEYS[6], ARGV[8])
        redis.call('DEL', KEYS[5], KEYS[1])

        return { accountId, sub, scope, clientId, ARGV[1] }
        """;

    private static readonly RedisValue[] Fields =
    [
        "access_token",
        "account_id",
        "sub",
        "scope",
        "client_id",
        "stable_refresh"
    ];

    private static readonly RedisValue[] ReplayFields =
    [
        "access_token",
        "refresh_token",
        "account_id",
        "sub",
        "scope",
        "client_id",
        "gen",
        "client_gen"
    ];

    public async ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();

        // Read the current generation for this sub so the token can be invalidated later
        var genValue = await db.StringGetAsync(KeyNames.UserGen(sub)).WaitAsync(cancellationToken);
        var gen = genValue.IsNullOrEmpty ? 0L : (long)genValue;
        var clientGenValue = await db.StringGetAsync(KeyNames.ClientUserGen(sub, clientId)).WaitAsync(cancellationToken);
        var clientGen = clientGenValue.IsNullOrEmpty ? 0L : (long)clientGenValue;
        var stableRefresh = ScopePolicy.HasOfflineAccess(scope);

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
            new("gen", gen),
            new("client_gen", clientGen),
            new("stable_refresh", stableRefresh ? "1" : "0")
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
        var db = multiplexer.GetDatabase();

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

        var stableRefresh = fields[5] == "1";
        if (!stableRefresh && fields[0] != accessToken)
        {
            return null;
        }

        // Check the token generation against the user's current generation to detect
        // whether all tokens were invalidated (e.g. after a password change).
        var sub = fields[2]!.ToString();
        var batch = db.CreateBatch();
        var storedGenTask = batch.HashGetAsync(refreshKey, "gen");
        var currentGenTask = batch.StringGetAsync(KeyNames.UserGen(sub));
        var storedClientGenTask = batch.HashGetAsync(refreshKey, "client_gen");
        var clientId = fields[4]!.ToString();
        var currentClientGenTask = batch.StringGetAsync(KeyNames.ClientUserGen(sub, clientId));
        batch.Execute();

        var storedGenValue = await storedGenTask.WaitAsync(cancellationToken);
        var currentGenValue = await currentGenTask.WaitAsync(cancellationToken);
        var storedClientGenValue = await storedClientGenTask.WaitAsync(cancellationToken);
        var currentClientGenValue = await currentClientGenTask.WaitAsync(cancellationToken);

        var storedGen = storedGenValue.IsNullOrEmpty ? 0L : (long)storedGenValue;
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        var storedClientGen = storedClientGenValue.IsNullOrEmpty ? 0L : (long)storedClientGenValue;
        var currentClientGen = currentClientGenValue.IsNullOrEmpty ? 0L : (long)currentClientGenValue;
        if (storedGen != currentGen || storedClientGen != currentClientGen)
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
            ClientId = clientId
        };
    }

    public async ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
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
        var clientId = fields[4]!.ToString();
        var batch = db.CreateBatch();
        var storedGenTask = batch.HashGetAsync(refreshKey, "gen");
        var currentGenTask = batch.StringGetAsync(KeyNames.UserGen(sub));
        var storedClientGenTask = batch.HashGetAsync(refreshKey, "client_gen");
        var currentClientGenTask = batch.StringGetAsync(KeyNames.ClientUserGen(sub, clientId));
        batch.Execute();

        var storedGenValue = await storedGenTask.WaitAsync(cancellationToken);
        var currentGenValue = await currentGenTask.WaitAsync(cancellationToken);
        var storedClientGenValue = await storedClientGenTask.WaitAsync(cancellationToken);
        var currentClientGenValue = await currentClientGenTask.WaitAsync(cancellationToken);
        var storedGen = storedGenValue.IsNullOrEmpty ? 0L : (long)storedGenValue;
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        var storedClientGen = storedClientGenValue.IsNullOrEmpty ? 0L : (long)storedClientGenValue;
        var currentClientGen = currentClientGenValue.IsNullOrEmpty ? 0L : (long)currentClientGenValue;
        if (storedGen != currentGen || storedClientGen != currentClientGen)
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

    public ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
    {
        return RefreshAccessCoreAsync(refreshToken, null, expire, refreshTokenExpire, cancellationToken);
    }

    public ValueTask<Access?> RefreshAccessAsync(string refreshToken, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client ID is required.", nameof(clientId));
        }

        return RefreshAccessCoreAsync(refreshToken, clientId, expire, refreshTokenExpire, cancellationToken);
    }

    private async ValueTask<Access?> RefreshAccessCoreAsync(string refreshToken, string? expectedClientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken)
    {
        var db = multiplexer.GetDatabase();
        var refreshKey = KeyNames.Refresh(refreshToken);
        var replayKey = KeyNames.RefreshReplay(refreshToken);

        var refreshFields = await db.HashGetAsync(refreshKey, ["access_token", "sub", "client_id"]).WaitAsync(cancellationToken);
        var oldAccessToken = refreshFields[0];
        var sub = refreshFields[1];
        var refreshClientId = refreshFields[2];
        if (oldAccessToken.IsNullOrEmpty || sub.IsNullOrEmpty || refreshClientId.IsNullOrEmpty)
        {
            return await ReadRefreshReplayAsync(db, refreshToken, expectedClientId, cancellationToken);
        }

        var newAccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var result = await db.ScriptEvaluateAsync(
            RotateRefreshTokenScript,
            [
                refreshKey,
                KeyNames.Access(newAccessToken),
                KeyNames.Refresh(newRefreshToken),
                KeyNames.UserGen(sub!),
                KeyNames.Access(oldAccessToken!),
                replayKey,
                KeyNames.ClientUserGen(sub!.ToString(), refreshClientId.ToString())
            ],
            [
                newRefreshToken,
                newAccessToken,
                ToPositiveMilliseconds(expire),
                ToPositiveMilliseconds(refreshTokenExpire),
                sub!,
                oldAccessToken!,
                expectedClientId ?? string.Empty,
                ToPositiveMilliseconds(RefreshReplayWindow),
                refreshToken
            ]).WaitAsync(cancellationToken);

        if (result.IsNull)
        {
            return await ReadRefreshReplayAsync(db, refreshToken, expectedClientId, cancellationToken);
        }

        var accessFields = (RedisResult[]?)result;
        if (accessFields is not { Length: 5 })
        {
            return null;
        }

        var accountId = GetString(accessFields[0]);
        var refreshedSub = GetString(accessFields[1]);
        var scope = GetString(accessFields[2]);
        var clientId = GetString(accessFields[3]);
        var refreshedRefreshToken = GetString(accessFields[4]);
        if (string.IsNullOrWhiteSpace(accountId) ||
            string.IsNullOrWhiteSpace(refreshedSub) ||
            string.IsNullOrWhiteSpace(scope) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(refreshedRefreshToken))
        {
            return null;
        }

        return new Access
        {
            Id = accountId,
            Sub = refreshedSub,
            AccessToken = newAccessToken,
            RefreshToken = refreshedRefreshToken,
            Scope = scope,
            ClientId = clientId
        };
    }

    private async ValueTask<Access?> ReadRefreshReplayAsync(IDatabase db, string refreshToken, string? expectedClientId, CancellationToken cancellationToken)
    {
        var replayKey = KeyNames.RefreshReplay(refreshToken);
        var fields = await db.HashGetAsync(replayKey, ReplayFields).WaitAsync(cancellationToken);
        if (fields.Length != ReplayFields.Length || fields.Any(static p => p.IsNullOrEmpty))
        {
            return null;
        }

        var accessToken = fields[0].ToString();
        var replayedRefreshToken = fields[1].ToString();
        var accountId = fields[2].ToString();
        var sub = fields[3].ToString();
        var scope = fields[4].ToString();
        var clientId = fields[5].ToString();

        if (!string.IsNullOrEmpty(expectedClientId) && clientId != expectedClientId)
        {
            return null;
        }

        var currentGenValue = await db.StringGetAsync(KeyNames.UserGen(sub)).WaitAsync(cancellationToken);
        var storedGen = (long)fields[6];
        var currentGen = currentGenValue.IsNullOrEmpty ? 0L : (long)currentGenValue;
        if (storedGen != currentGen)
        {
            return null;
        }

        var currentClientGenValue = await db.StringGetAsync(KeyNames.ClientUserGen(sub, clientId)).WaitAsync(cancellationToken);
        var storedClientGen = (long)fields[7];
        var currentClientGen = currentClientGenValue.IsNullOrEmpty ? 0L : (long)currentClientGenValue;
        if (storedClientGen != currentClientGen)
        {
            return null;
        }

        var activeRefreshToken = await db.StringGetAsync(KeyNames.Access(accessToken)).WaitAsync(cancellationToken);
        if (activeRefreshToken != replayedRefreshToken)
        {
            return null;
        }

        var activeAccessToken = await db.HashGetAsync(KeyNames.Refresh(replayedRefreshToken), "access_token").WaitAsync(cancellationToken);
        if (activeAccessToken != accessToken)
        {
            return null;
        }

        logger.LogDebug("Refresh token replay served from idempotent window. ClientId: {ClientId}", clientId);

        return new Access
        {
            Id = accountId,
            Sub = sub,
            AccessToken = accessToken,
            RefreshToken = replayedRefreshToken,
            Scope = scope,
            ClientId = clientId
        };
    }

    public async ValueTask RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();

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
        var db = multiplexer.GetDatabase();
        var userGenKey = KeyNames.UserGen(sub);

        // Increment the generation counter. Any existing token (access or refresh) that
        // was issued with a lower generation value will be rejected by Verify/Refresh.
        await db.StringIncrementAsync(userGenKey).WaitAsync(cancellationToken);

        // Keep the key alive long enough to outlast any existing long-lived refresh tokens.
        await db.KeyExpireAsync(userGenKey, TimeSpan.FromDays(90)).WaitAsync(cancellationToken);
    }

    public async ValueTask InvalidateClientTokensAsync(string sub, string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var db = multiplexer.GetDatabase();
        var clientUserGenKey = KeyNames.ClientUserGen(sub, clientId);

        await db.StringIncrementAsync(clientUserGenKey).WaitAsync(cancellationToken);
        await db.KeyExpireAsync(clientUserGenKey, TimeSpan.FromDays(90)).WaitAsync(cancellationToken);
    }

    private static long ToPositiveMilliseconds(TimeSpan value)
    {
        return Math.Max(1, (long)Math.Ceiling(value.TotalMilliseconds));
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
