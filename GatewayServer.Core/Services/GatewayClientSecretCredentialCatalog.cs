using System.Security.Cryptography;
using System.Text;
using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IGatewayClientSecretCredentialWriter
{
    void Publish(GatewayClientSecretCredentialSnapshot snapshot);
}

internal sealed class GatewayClientSecretCredentialCatalog :
    IGatewayClientStaticTokenValidator,
    IGatewayClientSecretCredentialWriter
{
    private const string AccessTokenPrefix = "gwc_";

    private readonly object m_Sync = new();
    private Dictionary<string, SecretCredential> m_Secrets = new(StringComparer.Ordinal);

    public ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        return ValidateStaticTokenAsync(accessToken, cancellationToken);
    }

    public ValueTask<GatewayClientTokenValidationResult> ValidateStaticTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!TryParseAccessToken(accessToken, out var tokenId, out var secret))
        {
            return Rejected("Invalid Gateway client access token.");
        }

        SecretCredential? credential;
        lock (m_Sync)
        {
            if (m_Secrets.Count == 0)
            {
                return Rejected("Gateway client secret credentials are not configured.");
            }

            m_Secrets.TryGetValue(tokenId, out credential);
        }

        if (credential == null)
        {
            return Rejected("Invalid Gateway client access token.");
        }

        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(candidateHash, credential.SecretHash))
            {
                return Rejected("Invalid Gateway client access token.");
            }

            return ValueTask.FromResult(
                GatewayClientTokenValidationResult.Accepted(
                    new GatewayClientPrincipal(credential.SubjectId)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateHash);
        }
    }

    public void Publish(GatewayClientSecretCredentialSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var secrets = new Dictionary<string, SecretCredential>(StringComparer.Ordinal);
        foreach (var secret in snapshot.Secrets)
        {
            if (string.IsNullOrWhiteSpace(secret.TokenId) ||
                string.IsNullOrWhiteSpace(secret.SubjectId) ||
                string.IsNullOrWhiteSpace(secret.SecretHash))
            {
                continue;
            }

            secrets[secret.TokenId.Trim()] = new SecretCredential(
                secret.SubjectId.Trim(),
                Convert.FromBase64String(secret.SecretHash));
        }

        lock (m_Sync)
        {
            m_Secrets = secrets;
        }
    }

    private static bool TryParseAccessToken(
        string accessToken,
        out string tokenId,
        out string secret)
    {
        tokenId = string.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var normalizedToken = accessToken.Trim();
        if (!normalizedToken.StartsWith(AccessTokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int separatorIndex = normalizedToken.IndexOf('.', AccessTokenPrefix.Length);
        if (separatorIndex <= AccessTokenPrefix.Length ||
            separatorIndex == normalizedToken.Length - 1)
        {
            return false;
        }

        tokenId = normalizedToken.Substring(
            AccessTokenPrefix.Length,
            separatorIndex - AccessTokenPrefix.Length);
        secret = normalizedToken.Substring(separatorIndex + 1);
        return !string.IsNullOrWhiteSpace(tokenId) &&
               !string.IsNullOrWhiteSpace(secret);
    }

    private static ValueTask<GatewayClientTokenValidationResult> Rejected(string errorMessage)
    {
        return ValueTask.FromResult(GatewayClientTokenValidationResult.Rejected(errorMessage));
    }

    private sealed record SecretCredential(
        string SubjectId,
        byte[] SecretHash);
}
