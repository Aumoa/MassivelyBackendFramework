using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayClientAuthenticateRequest
{
    public const ushort ProtocolVersion = 1;

    public GatewayClientAuthenticateRequest(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Gateway client access token is required.", nameof(accessToken));
        }

        AccessToken = accessToken.Trim();
    }

    public string AccessToken { get; }

    public static IPacketCodec<GatewayClientAuthenticateRequest> Codec { get; } = new GatewayClientAuthenticateRequestCodec();

    private sealed class GatewayClientAuthenticateRequestCodec : IPacketCodec<GatewayClientAuthenticateRequest>
    {
        public int GetPayloadSize(GatewayClientAuthenticateRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.AccessToken);
        }

        public void Encode(GatewayClientAuthenticateRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.AccessToken);
        }

        public GatewayClientAuthenticateRequest Decode(ref PacketReader reader)
        {
            return new GatewayClientAuthenticateRequest(reader.ReadString());
        }
    }
}
