using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayClientAuthenticationOptionsRequest
{
    public const ushort ProtocolVersion = 1;

    public GatewayClientAuthenticationOptionsRequest(string backendKind)
        : this(backendKind, serverHandle: null)
    {
    }

    public GatewayClientAuthenticationOptionsRequest(
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

    public static IPacketCodec<GatewayClientAuthenticationOptionsRequest> Codec { get; } = new GatewayClientAuthenticationOptionsRequestCodec();

    private sealed class GatewayClientAuthenticationOptionsRequestCodec : IPacketCodec<GatewayClientAuthenticationOptionsRequest>
    {
        public int GetPayloadSize(GatewayClientAuthenticationOptionsRequest value)
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

        public void Encode(GatewayClientAuthenticationOptionsRequest value, ref PacketWriter writer)
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

        public GatewayClientAuthenticationOptionsRequest Decode(ref PacketReader reader)
        {
            var backendKind = reader.ReadString();
            var hasServerHandle = reader.ReadByte() != 0;
            return new GatewayClientAuthenticationOptionsRequest(
                backendKind,
                hasServerHandle
                    ? new GatewayBackendServerHandle(reader.ReadString())
                    : null);
        }
    }
}
