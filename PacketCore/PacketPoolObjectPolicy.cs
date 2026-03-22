using Microsoft.Extensions.ObjectPool;

namespace PacketCore;

internal class PacketPoolObjectPolicy : IPooledObjectPolicy<Packet>
{
    public Packet Create()
    {
        return new Packet();
    }

    public bool Return(Packet obj)
    {
        obj.ReadyForReuse();
        return true;
    }
}
