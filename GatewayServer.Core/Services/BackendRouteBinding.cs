using System;

namespace GatewayServer.Services;

internal sealed record BackendRouteBinding
{
    public BackendRouteBinding(
        string backendKind,
        string nodeId,
        string masterConnectionId,
        string directConnectionId)
    {
        BackendKind = RequireValue(backendKind, nameof(backendKind));
        NodeId = RequireValue(nodeId, nameof(nodeId));
        MasterConnectionId = RequireValue(masterConnectionId, nameof(masterConnectionId));
        DirectConnectionId = RequireValue(directConnectionId, nameof(directConnectionId));
    }

    public string BackendKind { get; }

    public string NodeId { get; }

    public string MasterConnectionId { get; }

    public string DirectConnectionId { get; }

    public bool Matches(
        string backendKind,
        string nodeId,
        string masterConnectionId,
        string directConnectionId)
    {
        return string.Equals(BackendKind, backendKind, StringComparison.Ordinal) &&
               string.Equals(NodeId, nodeId, StringComparison.Ordinal) &&
               string.Equals(MasterConnectionId, masterConnectionId, StringComparison.Ordinal) &&
               string.Equals(DirectConnectionId, directConnectionId, StringComparison.Ordinal);
    }

    private static string RequireValue(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Backend route binding values are required.", parameterName);
        }

        return value.Trim();
    }
}
