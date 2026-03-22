using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PacketCore.Utility;

public readonly struct RentedArray<T> : IDisposable
{
    private readonly T[] m_RentArray;
    private readonly int m_Length;

    public RentedArray(T[] rentArray, int length)
    {
        m_RentArray = rentArray;
        m_Length = length;
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(m_RentArray);
    }

    public int Length => m_Length;

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref m_RentArray[index];
    }

    public Memory<T> AsMemory() => m_RentArray.AsMemory(0, m_Length);

    public Memory<T> AsMemory(int start) => m_RentArray.AsMemory(start, m_Length - start);

    public readonly ReadOnlyMemory<T> AsReadOnlyMemory() => m_RentArray.AsMemory(0, m_Length);

    public readonly ReadOnlyMemory<T> AsReadOnlyMemory(int start) => m_RentArray.AsMemory(start, m_Length - start);
    public Span<T> AsSpan() => m_RentArray.AsSpan(0, m_Length);

    public Span<T> AsSpan(int start, int length) => m_RentArray.AsSpan(start, length);

    public readonly ReadOnlySpan<T> AsReadOnlySpan() => m_RentArray.AsSpan(0, m_Length);

    public readonly ReadOnlySpan<T> AsReadOnlySpan(int start, int length) => m_RentArray.AsSpan(start, length);

    public T[] RentArray => m_RentArray;

    public static implicit operator bool(RentedArray<T> rentedArray) => rentedArray.m_RentArray != null;

    public bool IsEmpty => m_RentArray == null;

    public static RentedArray<T> Get(int sizeInBytes)
    {
        return new RentedArray<T>(ArrayPool<T>.Shared.Rent(sizeInBytes), sizeInBytes);
    }
}
