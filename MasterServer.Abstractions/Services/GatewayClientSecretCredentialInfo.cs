using System;

namespace MasterServer.Services;

public sealed class GatewayClientSecretCredentialInfo
{
    public GatewayClientSecretCredentialInfo(
        long id,
        string tokenId,
        string subjectId,
        string displayName,
        bool enabled,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TokenId = tokenId ?? throw new ArgumentNullException(nameof(tokenId));
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public long Id { get; }

    public string TokenId { get; }

    public string SubjectId { get; }

    public string DisplayName { get; }

    public bool Enabled { get; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }
}
