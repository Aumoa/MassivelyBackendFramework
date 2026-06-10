using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class ServiceAdminStatusResponse
{
    private const int MaxStatusItemCount = 512;

    public ServiceAdminStatusResponse(
        Guid requestId,
        bool success,
        MasterNodeKind nodeKind,
        string nodeId,
        string displayName,
        string masterConnectionId,
        ServiceAdminStatusItem[] items,
        string errorMessage,
        DateTimeOffset observedAt)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (nodeKind is not (MasterNodeKind.Gateway or MasterNodeKind.Dedicated or MasterNodeKind.MasterAdmin or MasterNodeKind.RemoteDebug or MasterNodeKind.Unknown))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeKind));
        }

        RequestId = requestId;
        Success = success;
        NodeKind = nodeKind;
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        MasterConnectionId = masterConnectionId ?? throw new ArgumentNullException(nameof(masterConnectionId));
        Items = items ?? throw new ArgumentNullException(nameof(items));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
        ObservedAt = observedAt;
    }

    public Guid RequestId { get; }

    public bool Success { get; }

    public MasterNodeKind NodeKind { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string MasterConnectionId { get; }

    public ServiceAdminStatusItem[] Items { get; }

    public string ErrorMessage { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<ServiceAdminStatusResponse> Codec { get; } = new ServiceAdminStatusResponseCodec();

    public static ServiceAdminStatusResponse Failure(
        Guid requestId,
        string targetConnectionId,
        string errorMessage)
    {
        return new ServiceAdminStatusResponse(
            requestId,
            success: false,
            MasterNodeKind.Unknown,
            string.Empty,
            string.Empty,
            targetConnectionId,
            Array.Empty<ServiceAdminStatusItem>(),
            errorMessage,
            DateTimeOffset.UtcNow);
    }

    private sealed class ServiceAdminStatusResponseCodec : IPacketCodec<ServiceAdminStatusResponse>
    {
        public int GetPayloadSize(ServiceAdminStatusResponse value)
        {
            int size = 16 +
                       sizeof(byte) +
                       sizeof(byte) +
                       PacketWriter.GetStringSize(value.NodeId) +
                       PacketWriter.GetStringSize(value.DisplayName) +
                       PacketWriter.GetStringSize(value.MasterConnectionId) +
                       sizeof(int) +
                       PacketWriter.GetStringSize(value.ErrorMessage) +
                       sizeof(long);

            foreach (var item in value.Items)
            {
                size += GetItemSize(item);
            }

            return size;
        }

        public void Encode(ServiceAdminStatusResponse value, ref PacketWriter writer)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte(value.Success ? (byte)1 : (byte)0);
            writer.WriteByte((byte)value.NodeKind);
            writer.WriteString(value.NodeId);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.MasterConnectionId);
            writer.WriteInt32(value.Items.Length);

            foreach (var item in value.Items)
            {
                WriteItem(item, ref writer);
            }

            writer.WriteString(value.ErrorMessage);
            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public ServiceAdminStatusResponse Decode(ref PacketReader reader)
        {
            var requestId = reader.ReadGuid();
            bool success = reader.ReadByte() != 0;
            var nodeKind = (MasterNodeKind)reader.ReadByte();
            string nodeId = reader.ReadString();
            string displayName = reader.ReadString();
            string masterConnectionId = reader.ReadString();
            int itemCount = reader.ReadInt32();
            if (itemCount < 0 || itemCount > MaxStatusItemCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid service admin status item count.");
            }

            var items = new ServiceAdminStatusItem[itemCount];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = ReadItem(ref reader);
            }

            string errorMessage = reader.ReadString();
            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new ServiceAdminStatusResponse(
                requestId,
                success,
                nodeKind,
                nodeId,
                displayName,
                masterConnectionId,
                items,
                errorMessage,
                observedAt);
        }

        private static int GetItemSize(ServiceAdminStatusItem item)
        {
            return PacketWriter.GetStringSize(item.Group) +
                   PacketWriter.GetStringSize(item.Name) +
                   PacketWriter.GetStringSize(item.Value);
        }

        private static void WriteItem(ServiceAdminStatusItem item, ref PacketWriter writer)
        {
            writer.WriteString(item.Group);
            writer.WriteString(item.Name);
            writer.WriteString(item.Value);
        }

        private static ServiceAdminStatusItem ReadItem(ref PacketReader reader)
        {
            return new ServiceAdminStatusItem(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
