using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class DirectConnectCodeResponse
{
    public DirectConnectCodeResponse(
        Guid requestId,
        bool success,
        string code,
        DateTimeOffset expiresAt,
        string errorMessage)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (code == null)
        {
            throw new ArgumentNullException(nameof(code));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        RequestId = requestId;
        Success = success;
        Code = code;
        ExpiresAt = expiresAt;
        ErrorMessage = errorMessage;
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public string Code { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string ErrorMessage { get; }

    public static DirectConnectCodeResponse Failure(Guid requestId, string errorMessage)
    {
        return new DirectConnectCodeResponse(requestId, false, string.Empty, DateTimeOffset.UnixEpoch, errorMessage);
    }

    public static IPacketCodec<DirectConnectCodeResponse> Codec { get; } = new DirectConnectCodeResponseCodec();

    private sealed class DirectConnectCodeResponseCodec : IPacketCodec<DirectConnectCodeResponse>
    {
        public int GetPayloadSize(DirectConnectCodeResponse value)
        {
            return sizeof(long) + sizeof(long) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.Code) +
                   sizeof(long) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(DirectConnectCodeResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.Code);
            writer.WriteInt64(value.ExpiresAt.ToUnixTimeMilliseconds());
            writer.WriteString(value.ErrorMessage);
        }

        public DirectConnectCodeResponse Decode(ref PacketReader reader)
        {
            return new DirectConnectCodeResponse(
                reader.ReadGuid(),
                reader.ReadByte() != 0,
                reader.ReadString(),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()),
                reader.ReadString());
        }
    }
}
