using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendRouteOpenRequest
{
    public const ushort ProtocolVersion = 2;

    public GatewayBackendRouteOpenRequest(string backendKind)
        : this(backendKind, serverHandle: null)
    {
    }

    public GatewayBackendRouteOpenRequest(
        string backendKind,
        GatewayBackendServerHandle? serverHandle)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        BackendKind = backendKind.Trim();
        ServerHandle = serverHandle;
    }

    public string BackendKind { get; }

    public GatewayBackendServerHandle? ServerHandle { get; }

    public static IPacketCodec<GatewayBackendRouteOpenRequest> Codec { get; } = new GatewayBackendRouteOpenRequestCodec();

    private sealed class GatewayBackendRouteOpenRequestCodec : IPacketCodec<GatewayBackendRouteOpenRequest>
    {
        public int GetPayloadSize(GatewayBackendRouteOpenRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(byte) +
                   (value.ServerHandle == null
                       ? 0
                       : PacketWriter.GetStringSize(value.ServerHandle.Value));
        }

        public void Encode(GatewayBackendRouteOpenRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            if (value.ServerHandle == null)
            {
                writer.WriteByte(0);
            }
            else
            {
                writer.WriteByte(1);
                writer.WriteString(value.ServerHandle.Value);
            }
        }

        public GatewayBackendRouteOpenRequest Decode(ref PacketReader reader)
        {
            var backendKind = reader.ReadString();
            var hasServerHandle = reader.ReadByte() != 0;
            return new GatewayBackendRouteOpenRequest(
                backendKind,
                hasServerHandle
                    ? new GatewayBackendServerHandle(reader.ReadString())
                    : null);
        }
    }
}
