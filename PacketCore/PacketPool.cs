using System;
using Microsoft.Extensions.ObjectPool;

namespace PacketCore;

public static class PacketPool
{
    private static readonly ObjectPool<Packet> s_Pool = new DefaultObjectPool<Packet>(new PacketPoolObjectPolicy());

    public readonly struct ScopedPacket : IDisposable
    {
        private readonly ObjectPool<Packet> m_Pool;
        private readonly Packet m_Packet;

        public ScopedPacket(ObjectPool<Packet> pool, Packet packet)
        {
            m_Pool = pool;
            m_Packet = packet;
        }

        public void Dispose()
        {
            m_Pool.Return(m_Packet);
        }
    }

    public static Packet Get()
    {
        return s_Pool.Get();
    }

    public static ScopedPacket Get(out Packet packet)
    {
        packet = Get();
        return new ScopedPacket(s_Pool, packet);
    }

    public static void Return(Packet packet)
    {
        s_Pool.Return(packet);
    }
}
