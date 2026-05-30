using System;

namespace MasterServer.ControlPlane;

public sealed class DedicatedNodeEndpoint
{
    public DedicatedNodeEndpoint(
        string nodeId,
        string displayName,
        string masterConnectionId,
        MasterSocketEndpoint gatewayEndpoint,
        DateTimeOffset advertisedAt)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node id is required.", nameof(nodeId));
        }

        if (displayName == null)
        {
            throw new ArgumentNullException(nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(masterConnectionId))
        {
            throw new ArgumentException("Master connection id is required.", nameof(masterConnectionId));
        }

        NodeId = nodeId;
        DisplayName = displayName;
        MasterConnectionId = masterConnectionId;
        GatewayEndpoint = gatewayEndpoint ?? throw new ArgumentNullException(nameof(gatewayEndpoint));
        AdvertisedAt = advertisedAt;
    }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string MasterConnectionId { get; }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public DateTimeOffset AdvertisedAt { get; }
}
