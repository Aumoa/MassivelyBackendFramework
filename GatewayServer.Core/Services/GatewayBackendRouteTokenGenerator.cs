using System.Security.Cryptography;
using System.Text;
using GatewayServer.Protocols;

namespace GatewayServer.Services;

internal interface IGatewayBackendRouteTokenGenerator
{
    GatewayBackendRouteToken Generate();

    string GetFingerprint(GatewayBackendRouteToken routeToken);
}

internal sealed class GatewayBackendRouteTokenGenerator : IGatewayBackendRouteTokenGenerator
{
    private const int TokenByteLength = 32;
    private const int FingerprintByteLength = 6;

    public GatewayBackendRouteToken Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return new GatewayBackendRouteToken(ToBase64Url(bytes));
    }

    public string GetFingerprint(GatewayBackendRouteToken routeToken)
    {
        if (routeToken == null)
        {
            throw new ArgumentNullException(nameof(routeToken));
        }

        byte[] valueBytes = Encoding.UTF8.GetBytes(routeToken.Value);
        byte[] hash = SHA256.HashData(valueBytes);
        return Convert.ToHexString(hash.AsSpan(0, FingerprintByteLength));
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        string token = Convert.ToBase64String(bytes);
        return token
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
