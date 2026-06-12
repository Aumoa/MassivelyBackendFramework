using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterServer.ControlPlane;
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
if not value then
  return nil
end
for i = 1, #ARGV do
  if string.find(value, ARGV[i], 1, true) == nil then
    return nil
  end
end
redis.call('DEL', KEYS[1])
return value
""";

    private readonly IDatabase m_Database = redis.GetDatabase();
    private readonly DirectConnectCodeOptions m_Options = options.Value;

    public async ValueTask<DirectConnectCodeTicket> CreateAsync(
        string gatewayMasterConnectionId,
        string gatewayNodeId,
        MasterNodeKind targetNodeKind,
        string targetMasterConnectionId,
        string targetNodeId,
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

        if (targetNodeKind is not (MasterNodeKind.Dedicated or MasterNodeKind.Backend))
        {
            throw new ArgumentOutOfRangeException(nameof(targetNodeKind));
        }

        if (string.IsNullOrWhiteSpace(targetMasterConnectionId))
        {
            throw new ArgumentException("Target Master connection id is required.", nameof(targetMasterConnectionId));
        }

        if (string.IsNullOrWhiteSpace(targetNodeId))
        {
            throw new ArgumentException("Target node id is required.", nameof(targetNodeId));
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
                targetNodeKind,
                targetMasterConnectionId,
                targetNodeId,
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
        string expectedGatewayMasterConnectionId,
        string expectedGatewayNodeId,
        MasterNodeKind expectedTargetNodeKind,
        string expectedTargetMasterConnectionId,
        string expectedTargetNodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(expectedGatewayMasterConnectionId) ||
            string.IsNullOrWhiteSpace(expectedGatewayNodeId) ||
            string.IsNullOrWhiteSpace(expectedTargetMasterConnectionId) ||
            string.IsNullOrWhiteSpace(expectedTargetNodeId))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        RedisValue[] expectedFields =
        [
            CreateJsonStringMatch(nameof(DirectConnectCodeTicket.GatewayMasterConnectionId), expectedGatewayMasterConnectionId),
            CreateJsonStringMatch(nameof(DirectConnectCodeTicket.GatewayNodeId), expectedGatewayNodeId),
            CreateJsonNumberMatch(nameof(DirectConnectCodeTicket.TargetNodeKind), (int)expectedTargetNodeKind),
            CreateJsonStringMatch(nameof(DirectConnectCodeTicket.TargetMasterConnectionId), expectedTargetMasterConnectionId),
            CreateJsonStringMatch(nameof(DirectConnectCodeTicket.TargetNodeId), expectedTargetNodeId)
        ];
        var result = await m_Database.ScriptEvaluateAsync(
            ConsumeScript,
            [GetKey(code)],
            expectedFields).ConfigureAwait(false);
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

    private static string CreateJsonStringMatch(string propertyName, string value)
    {
        return $"\"{propertyName}\":{JsonSerializer.Serialize(value)}";
    }

    private static string CreateJsonNumberMatch(string propertyName, int value)
    {
        return $"\"{propertyName}\":{value}";
    }
}
