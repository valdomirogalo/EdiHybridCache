using System.Buffers;

namespace EdiHybridCache.Cache;

/// <summary>
/// Owns a <see cref="byte"/> array rented from <see cref="ArrayPool{T}.Shared"/> for the
/// duration of a decompression. Read via <see cref="Span"/> inside a <c>using</c> scope and
/// <see cref="Dispose"/> returns the buffer to the pool — this avoids materializing a second
/// copy of the decompressed payload before deserialization.
/// </summary>
internal readonly struct PooledBuffer : IDisposable
{
    private readonly byte[]? _buffer;
    private readonly int _length;

    internal PooledBuffer(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public int Length => _length;

    public ReadOnlySpan<byte> Span => _buffer is null ? default : _buffer.AsSpan(0, _length);

    public void Dispose()
    {
        if (_buffer is not null)
            ArrayPool<byte>.Shared.Return(_buffer);
    }
}
