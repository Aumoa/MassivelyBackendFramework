using System;

namespace MasterServer.ControlPlane;

public sealed class BackendNodeEndpoint
{
    public BackendNodeEndpoint(
        string backendKind,
        string nodeId,
        string displayName,
        string masterConnectionId,
        MasterSocketEndpoint gatewayEndpoint,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        DateTimeOffset advertisedAt)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

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

        BackendKind = backendKind;
        NodeId = nodeId;
        DisplayName = displayName;
        MasterConnectionId = masterConnectionId;
        GatewayEndpoint = gatewayEndpoint ?? throw new ArgumentNullException(nameof(gatewayEndpoint));
        ManifestId = manifestId;
        ManifestHash = manifestHash;
        AdvertisedAt = advertisedAt;
    }

    public string BackendKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string MasterConnectionId { get; }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public BackendPacketManifestId ManifestId { get; }

    public BackendPacketManifestHash ManifestHash { get; }

    public DateTimeOffset AdvertisedAt { get; }

    public static bool IsBackendNodeKind(MasterNodeKind nodeKind)
    {
        return nodeKind is MasterNodeKind.Backend;
    }
}
