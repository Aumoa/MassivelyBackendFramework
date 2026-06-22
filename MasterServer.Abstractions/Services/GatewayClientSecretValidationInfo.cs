using System;

namespace MasterServer.Services;

public sealed class GatewayClientSecretValidationInfo
{
    public GatewayClientSecretValidationInfo(
        string tokenId,
        string subjectId,
        string secretHash)
    {
        TokenId = tokenId ?? throw new ArgumentNullException(nameof(tokenId));
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        SecretHash = secretHash ?? throw new ArgumentNullException(nameof(secretHash));
    }

    public string TokenId { get; }

    public string SubjectId { get; }

    public string SecretHash { get; }
}
