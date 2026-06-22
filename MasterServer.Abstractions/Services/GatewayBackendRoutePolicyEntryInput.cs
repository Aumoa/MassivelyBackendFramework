using System;

namespace MasterServer.Services;

public sealed class GatewayBackendRoutePolicyEntryInput
{
    public GatewayBackendRoutePolicyEntryInput(
        string backendKind,
        bool enabled)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        BackendKind = backendKind.Trim();
        Enabled = enabled;
    }

    public string BackendKind { get; }

    public bool Enabled { get; }
}
