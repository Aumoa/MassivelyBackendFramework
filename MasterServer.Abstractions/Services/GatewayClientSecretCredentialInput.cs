using System;

namespace MasterServer.Services;

public sealed class GatewayClientSecretCredentialInput
{
    public GatewayClientSecretCredentialInput(
        string subjectId,
        string displayName,
        bool enabled)
    {
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Enabled = enabled;
    }

    public string SubjectId { get; }

    public string DisplayName { get; }

    public bool Enabled { get; }
}
