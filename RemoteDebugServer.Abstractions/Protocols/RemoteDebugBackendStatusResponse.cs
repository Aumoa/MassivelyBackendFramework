using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendStatusResponse
{
    public RemoteDebugBackendStatusResponse(
        string backendKind,
        int gatewayConnectionCount,
        int remoteDebugClientCount,
        long observedAtUnixTimeMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (gatewayConnectionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gatewayConnectionCount));
        }

        if (remoteDebugClientCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteDebugClientCount));
        }

        BackendKind = backendKind.Trim();
        GatewayConnectionCount = gatewayConnectionCount;
        RemoteDebugClientCount = remoteDebugClientCount;
        ObservedAtUnixTimeMilliseconds = observedAtUnixTimeMilliseconds;
    }

    public string BackendKind { get; }

    public int GatewayConnectionCount { get; }

    public int RemoteDebugClientCount { get; }

    public long ObservedAtUnixTimeMilliseconds { get; }

    public static IPacketCodec<RemoteDebugBackendStatusResponse> Codec { get; } = new RemoteDebugBackendStatusResponseCodec();

    private sealed class RemoteDebugBackendStatusResponseCodec : IPacketCodec<RemoteDebugBackendStatusResponse>
    {
        public int GetPayloadSize(RemoteDebugBackendStatusResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind) +
                   sizeof(int) +
                   sizeof(int) +
                   sizeof(long);
        }

        public void Encode(RemoteDebugBackendStatusResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            writer.WriteInt32(value.GatewayConnectionCount);
            writer.WriteInt32(value.RemoteDebugClientCount);
            writer.WriteInt64(value.ObservedAtUnixTimeMilliseconds);
        }

        public RemoteDebugBackendStatusResponse Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendStatusResponse(
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt64());
        }
    }
}
