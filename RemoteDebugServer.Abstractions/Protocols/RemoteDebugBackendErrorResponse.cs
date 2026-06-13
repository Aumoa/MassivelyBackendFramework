using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendErrorResponse
{
    public RemoteDebugBackendErrorResponse(
        ushort requestPacketId,
        ushort requestVersion,
        string errorMessage)
    {
        if (requestPacketId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestPacketId));
        }

        RequestPacketId = requestPacketId;
        RequestVersion = requestVersion;
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public ushort RequestPacketId { get; }

    public ushort RequestVersion { get; }

    public string ErrorMessage { get; }

    public static IPacketCodec<RemoteDebugBackendErrorResponse> Codec { get; } = new RemoteDebugBackendErrorResponseCodec();

    private sealed class RemoteDebugBackendErrorResponseCodec : IPacketCodec<RemoteDebugBackendErrorResponse>
    {
        public int GetPayloadSize(RemoteDebugBackendErrorResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return sizeof(ushort) +
                   sizeof(ushort) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(RemoteDebugBackendErrorResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteUInt16(value.RequestPacketId);
            writer.WriteUInt16(value.RequestVersion);
            writer.WriteString(value.ErrorMessage);
        }

        public RemoteDebugBackendErrorResponse Decode(ref PacketReader reader)
        {
            return new RemoteDebugBackendErrorResponse(
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadString());
        }
    }
}
