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
        : this(
            backendKind,
            nodeId,
            displayName,
            masterConnectionId,
            gatewayEndpoint,
            manifestId,
            manifestHash,
            BackendServerDescriptor.DefaultOpen,
            advertisedAt)
    {
    }

    public BackendNodeEndpoint(
        string backendKind,
        string nodeId,
        string displayName,
        string masterConnectionId,
        MasterSocketEndpoint gatewayEndpoint,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash manifestHash,
        BackendServerDescriptor descriptor,
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
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        AdvertisedAt = advertisedAt;
    }

    public string BackendKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string MasterConnectionId { get; }

    public MasterSocketEndpoint GatewayEndpoint { get; }

    public BackendPacketManifestId ManifestId { get; }

    public BackendPacketManifestHash ManifestHash { get; }

    public BackendServerDescriptor Descriptor { get; }

    public BackendNodeState State => Descriptor.State;

    public string DescriptorVersion => Descriptor.DescriptorVersion;

    public string DescriptorHash => Descriptor.DescriptorHash;

    public string DescriptorJson => Descriptor.DescriptorJson;

    public DateTimeOffset AdvertisedAt { get; }

    public static bool IsBackendNodeKind(MasterNodeKind nodeKind)
    {
        return nodeKind is MasterNodeKind.Backend;
    }
}
