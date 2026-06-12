using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterServer.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MasterServer.Services;

internal sealed class RedisDirectConnectCodeStore(
    IConnectionMultiplexer redis,
    IOptions<DirectConnectCodeOptions> options) : IDirectConnectCodeStore
{
    private const int CodeByteLength = 32;
    private const int MaxCreateAttempts = 4;
    private const string ConsumeScript = """
local value = redis.call('GET', KEYS[1])
if value then
  redis.call('DEL', KEYS[1])
end
return value
""";

    private readonly IDatabase m_Database = redis.GetDatabase();
    private readonly DirectConnectCodeOptions m_Options = options.Value;

    public async ValueTask<DirectConnectCodeTicket> CreateAsync(
        string gatewayMasterConnectionId,
        string gatewayNodeId,
        string dedicatedMasterConnectionId,
        string dedicatedNodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gatewayMasterConnectionId))
        {
            throw new ArgumentException("Gateway Master connection id is required.", nameof(gatewayMasterConnectionId));
        }

        if (string.IsNullOrWhiteSpace(gatewayNodeId))
        {
            throw new ArgumentException("Gateway node id is required.", nameof(gatewayNodeId));
        }

        if (string.IsNullOrWhiteSpace(dedicatedMasterConnectionId))
        {
            throw new ArgumentException("Dedicated Master connection id is required.", nameof(dedicatedMasterConnectionId));
        }

        if (string.IsNullOrWhiteSpace(dedicatedNodeId))
        {
            throw new ArgumentException("Dedicated node id is required.", nameof(dedicatedNodeId));
        }

        var ttl = GetTtl();
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);

        for (int i = 0; i < MaxCreateAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ticket = new DirectConnectCodeTicket(
                CreateCode(),
                gatewayMasterConnectionId,
                gatewayNodeId,
                dedicatedMasterConnectionId,
                dedicatedNodeId,
                expiresAt);
            bool created = await m_Database.StringSetAsync(
                GetKey(ticket.Code),
                JsonSerializer.Serialize(ticket),
                ttl,
                When.NotExists).ConfigureAwait(false);

            if (created)
            {
                return ticket;
            }
        }

        throw new InvalidOperationException("Failed to create a unique direct connect code.");
    }

    public async ValueTask<DirectConnectCodeTicket?> ConsumeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await m_Database.ScriptEvaluateAsync(
            ConsumeScript,
            [GetKey(code)],
            []).ConfigureAwait(false);
        if (result.IsNull)
        {
            return null;
        }

        var json = (string?)result;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var ticket = JsonSerializer.Deserialize<DirectConnectCodeTicket>(json);
        if (ticket == null ||
            ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return ticket;
    }

    private TimeSpan GetTtl()
    {
        return TimeSpan.FromMilliseconds(Math.Max(1000, m_Options.TimeToLiveMilliseconds));
    }

    private string GetKey(string code)
    {
        return $"{m_Options.RedisKeyPrefix}:{HashCode(code)}";
    }

    private static string CreateCode()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(CodeByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashCode(string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }
}
