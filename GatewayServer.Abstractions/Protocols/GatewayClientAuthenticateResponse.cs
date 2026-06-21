using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayClientAuthenticateResponse
{
    public const ushort ProtocolVersion = 1;

    public GatewayClientAuthenticateResponse(
        bool success,
        string subjectId,
        string errorMessage)
    {
        if (subjectId == null)
        {
            throw new ArgumentNullException(nameof(subjectId));
        }

        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        if (success && string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("Successful Gateway client authentication responses require a subject id.", nameof(subjectId));
        }

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Rejected Gateway client authentication responses require an error message.", nameof(errorMessage));
        }

        Success = success;
        SubjectId = subjectId.Trim();
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public string SubjectId { get; }

    public string ErrorMessage { get; }

    public static GatewayClientAuthenticateResponse Accepted(string subjectId)
    {
        return new GatewayClientAuthenticateResponse(true, subjectId, string.Empty);
    }

    public static GatewayClientAuthenticateResponse Rejected(string errorMessage)
    {
        return new GatewayClientAuthenticateResponse(false, string.Empty, errorMessage);
    }

    public static IPacketCodec<GatewayClientAuthenticateResponse> Codec { get; } = new GatewayClientAuthenticateResponseCodec();

    private sealed class GatewayClientAuthenticateResponseCodec : IPacketCodec<GatewayClientAuthenticateResponse>
    {
        public int GetPayloadSize(GatewayClientAuthenticateResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return sizeof(byte) +
                   PacketWriter.GetStringSize(value.SubjectId) +
                   PacketWriter.GetStringSize(value.ErrorMessage);
        }

        public void Encode(GatewayClientAuthenticateResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteString(value.SubjectId);
            writer.WriteString(value.ErrorMessage);
        }

        public GatewayClientAuthenticateResponse Decode(ref PacketReader reader)
        {
            var success = reader.ReadByte() != 0;
            var subjectId = reader.ReadString();
            var errorMessage = reader.ReadString();
            return new GatewayClientAuthenticateResponse(success, subjectId, errorMessage);
        }
    }
}
