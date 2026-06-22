using System;
using MasterServer.Services;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayClientSecretCredentialSnapshot
{
    private const int MaxSecretCount = 2048;

    public GatewayClientSecretCredentialSnapshot(
        GatewayClientSecretValidationInfo[] secrets,
        DateTimeOffset observedAt)
    {
        Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        ObservedAt = observedAt;
    }

    public GatewayClientSecretValidationInfo[] Secrets { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<GatewayClientSecretCredentialSnapshot> Codec { get; } = new GatewayClientSecretCredentialSnapshotCodec();

    private sealed class GatewayClientSecretCredentialSnapshotCodec : IPacketCodec<GatewayClientSecretCredentialSnapshot>
    {
        public int GetPayloadSize(GatewayClientSecretCredentialSnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = sizeof(int) + sizeof(long);
            foreach (var secret in value.Secrets)
            {
                size += PacketWriter.GetStringSize(secret.TokenId) +
                        PacketWriter.GetStringSize(secret.SubjectId) +
                        PacketWriter.GetStringSize(secret.SecretHash);
            }

            return size;
        }

        public void Encode(GatewayClientSecretCredentialSnapshot value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteInt32(value.Secrets.Length);
            foreach (var secret in value.Secrets)
            {
                writer.WriteString(secret.TokenId);
                writer.WriteString(secret.SubjectId);
                writer.WriteString(secret.SecretHash);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public GatewayClientSecretCredentialSnapshot Decode(ref PacketReader reader)
        {
            int secretCount = reader.ReadInt32();
            if (secretCount < 0 || secretCount > MaxSecretCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway client secret count.");
            }

            var secrets = new GatewayClientSecretValidationInfo[secretCount];
            for (int i = 0; i < secrets.Length; i++)
            {
                secrets[i] = new GatewayClientSecretValidationInfo(
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString());
            }

            return new GatewayClientSecretCredentialSnapshot(
                secrets,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()));
        }
    }
}
