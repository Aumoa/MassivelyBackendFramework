using System;
using System.Collections.Generic;
using System.Linq;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientListResponse
{
    public RemoteDebugBackendClientListResponse(
        IEnumerable<RemoteDebugBackendClientSnapshot> clients,
        long observedAtUnixTimeMilliseconds)
    {
        if (clients == null)
        {
            throw new ArgumentNullException(nameof(clients));
        }

        RemoteDebugBackendClientSnapshot[] clientArray = clients.ToArray();
        if (clientArray.Any(static client => client == null))
        {
            throw new ArgumentException("Client list cannot contain null entries.", nameof(clients));
        }

        Clients = Array.AsReadOnly(clientArray);
        ObservedAtUnixTimeMilliseconds = observedAtUnixTimeMilliseconds;
    }

    public IReadOnlyList<RemoteDebugBackendClientSnapshot> Clients { get; }

    public long ObservedAtUnixTimeMilliseconds { get; }

    public static IPacketCodec<RemoteDebugBackendClientListResponse> Codec { get; } = new RemoteDebugBackendClientListResponseCodec();

    private sealed class RemoteDebugBackendClientListResponseCodec : IPacketCodec<RemoteDebugBackendClientListResponse>
    {
        public int GetPayloadSize(RemoteDebugBackendClientListResponse value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int payloadSize = sizeof(int);
            for (int i = 0; i < value.Clients.Count; i++)
            {
                payloadSize += RemoteDebugBackendClientSnapshot.Codec.GetPayloadSize(value.Clients[i]);
            }

            return payloadSize + sizeof(long);
        }

        public void Encode(RemoteDebugBackendClientListResponse value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteInt32(value.Clients.Count);
            for (int i = 0; i < value.Clients.Count; i++)
            {
                RemoteDebugBackendClientSnapshot.Codec.Encode(value.Clients[i], ref writer);
            }

            writer.WriteInt64(value.ObservedAtUnixTimeMilliseconds);
        }

        public RemoteDebugBackendClientListResponse Decode(ref PacketReader reader)
        {
            int clientCount = reader.ReadInt32();
            if (clientCount < 0)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid RemoteDebug Backend client count.");
            }

            var clients = new RemoteDebugBackendClientSnapshot[clientCount];
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i] = RemoteDebugBackendClientSnapshot.Codec.Decode(ref reader);
            }

            long observedAtUnixTimeMilliseconds = reader.ReadInt64();
            return new RemoteDebugBackendClientListResponse(clients, observedAtUnixTimeMilliseconds);
        }
    }
}
