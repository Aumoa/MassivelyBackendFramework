using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendServerListEntry
{
    public GatewayBackendServerListEntry(
        GatewayBackendServerHandle serverHandle,
        string backendKind,
        GatewayBackendServerState state,
        string descriptorVersion,
        string descriptorHash,
        string descriptorJson)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (!Enum.IsDefined(typeof(GatewayBackendServerState), state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (string.IsNullOrWhiteSpace(descriptorVersion))
        {
            throw new ArgumentException("Descriptor version is required.", nameof(descriptorVersion));
        }

        if (string.IsNullOrWhiteSpace(descriptorHash))
        {
            throw new ArgumentException("Descriptor hash is required.", nameof(descriptorHash));
        }

        if (descriptorJson == null)
        {
            throw new ArgumentNullException(nameof(descriptorJson));
        }

        ServerHandle = serverHandle ?? throw new ArgumentNullException(nameof(serverHandle));
        BackendKind = backendKind.Trim();
        State = state;
        DescriptorVersion = descriptorVersion.Trim();
        DescriptorHash = descriptorHash.Trim();
        DescriptorJson = descriptorJson.Trim();
    }

    public GatewayBackendServerHandle ServerHandle { get; }

    public string BackendKind { get; }

    public GatewayBackendServerState State { get; }

    public string DescriptorVersion { get; }

    public string DescriptorHash { get; }

    public string DescriptorJson { get; }

    public static IPacketCodec<GatewayBackendServerListEntry> Codec { get; } = new GatewayBackendServerListEntryCodec();

    private sealed class GatewayBackendServerListEntryCodec : IPacketCodec<GatewayBackendServerListEntry>
    {
        public int GetPayloadSize(GatewayBackendServerListEntry value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.ServerHandle.Value) +
                   PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.DescriptorVersion) +
                   PacketWriter.GetStringSize(value.DescriptorHash) +
                   PacketWriter.GetStringSize(value.DescriptorJson);
        }

        public void Encode(GatewayBackendServerListEntry value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.ServerHandle.Value);
            writer.WriteString(value.BackendKind);
            writer.WriteByte((byte)value.State);
            writer.WriteString(value.DescriptorVersion);
            writer.WriteString(value.DescriptorHash);
            writer.WriteString(value.DescriptorJson);
        }

        public GatewayBackendServerListEntry Decode(ref PacketReader reader)
        {
            return new GatewayBackendServerListEntry(
                new GatewayBackendServerHandle(reader.ReadString()),
                reader.ReadString(),
                (GatewayBackendServerState)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
