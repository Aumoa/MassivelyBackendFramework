using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class MasterOverviewSnapshot
{
    private const int MaxConnectionCount = 8192;

    public MasterOverviewSnapshot(
        MasterSocketEndpoint socketEndpoint,
        MasterConnectionSnapshot[] connections,
        DateTimeOffset observedAt)
    {
        SocketEndpoint = socketEndpoint;
        Connections = connections;
        ObservedAt = observedAt;
    }

    public MasterSocketEndpoint SocketEndpoint { get; }

    public MasterConnectionSnapshot[] Connections { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<MasterOverviewSnapshot> Codec { get; } = new MasterOverviewSnapshotCodec();

    private sealed class MasterOverviewSnapshotCodec : IPacketCodec<MasterOverviewSnapshot>
    {
        public int GetPayloadSize(MasterOverviewSnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = GetEndpointSize(value.SocketEndpoint) + sizeof(int) + sizeof(long);
            foreach (var connection in value.Connections)
            {
                size += GetConnectionSize(connection);
            }

            return size;
        }

        public void Encode(MasterOverviewSnapshot value, ref PacketWriter writer)
        {
            WriteEndpoint(value.SocketEndpoint, ref writer);
            writer.WriteInt32(value.Connections.Length);

            foreach (var connection in value.Connections)
            {
                WriteConnection(connection, ref writer);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public MasterOverviewSnapshot Decode(ref PacketReader reader)
        {
            var endpoint = ReadEndpoint(ref reader);
            int connectionCount = reader.ReadInt32();
            if (connectionCount < 0 || connectionCount > MaxConnectionCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Master overview connection count.");
            }

            var connections = new MasterConnectionSnapshot[connectionCount];
            for (int i = 0; i < connections.Length; i++)
            {
                connections[i] = ReadConnection(ref reader);
            }

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new MasterOverviewSnapshot(endpoint, connections, observedAt);
        }

        private static int GetEndpointSize(MasterSocketEndpoint value)
        {
            return PacketWriter.GetStringSize(value.IPAddress) + sizeof(int) + sizeof(byte);
        }

        private static void WriteEndpoint(MasterSocketEndpoint value, ref PacketWriter writer)
        {
            writer.WriteString(value.IPAddress);
            writer.WriteInt32(value.Port);
            writer.WriteByte(value.UseTls ? (byte)1 : (byte)0);
        }

        private static MasterSocketEndpoint ReadEndpoint(ref PacketReader reader)
        {
            string ipAddress = reader.ReadString();
            int port = reader.ReadInt32();
            bool useTls = reader.ReadByte() != 0;
            return new MasterSocketEndpoint(ipAddress, port, useTls);
        }

        private static int GetConnectionSize(MasterConnectionSnapshot value)
        {
            return 16 +
                   PacketWriter.GetStringSize(value.RemoteEndPoint) +
                   sizeof(byte) +
                   sizeof(long) +
                   sizeof(long);
        }

        private static void WriteConnection(MasterConnectionSnapshot value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.ConnectionId);
            writer.WriteString(value.RemoteEndPoint);
            writer.WriteByte((byte)value.NodeKind);
            writer.WriteInt64(value.ConnectedAt.ToUnixTimeMilliseconds());
            writer.WriteInt64(value.LastSeenAt.ToUnixTimeMilliseconds());
        }

        private static MasterConnectionSnapshot ReadConnection(ref PacketReader reader)
        {
            var connectionId = reader.ReadGuid();
            string remoteEndPoint = reader.ReadString();
            var nodeKind = (MasterNodeKind)reader.ReadByte();
            var connectedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            var lastSeenAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());

            return new MasterConnectionSnapshot(connectionId, remoteEndPoint, nodeKind, connectedAt, lastSeenAt);
        }
    }
}
