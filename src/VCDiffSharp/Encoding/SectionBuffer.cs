using System.Buffers;
using System.Numerics;

namespace VCDiffSharp.Encoding;

/// <summary>Stages a window section in caller scratch, renting only on overflow.</summary>
internal ref struct SectionBuffer(Span<byte> scratch)
{
    private Span<byte> _buffer = scratch;
    private byte[]? _rented;
    internal int Length { get; private set; }
    internal readonly ReadOnlySpan<byte> Written => _buffer[..Length];

    internal void Add(byte value)
    {
        if (Length == _buffer.Length) Grow(1);
        _buffer[Length++] = value;
    }

    internal void Append(scoped ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > _buffer.Length - Length) Grow(bytes.Length);
        bytes.CopyTo(_buffer[Length..]);
        Length += bytes.Length;
    }

    internal void ReplaceLast(byte value) => _buffer[Length - 1] = value;

    internal void Integer(int value)
    {
        if (value < 128)
        {
            Add((byte)value);
            return;
        }
        if (_buffer.Length - Length < 5) Grow(5);
        Length += WireInteger.Write(_buffer[Length..], value);
    }

    private void Grow(int extra)
    {
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(Length + extra, 2 * _buffer.Length));
        Written.CopyTo(larger);
        Dispose();
        _rented = larger;
        _buffer = larger;
    }

    internal void Dispose()
    {
        if (_rented != null) ArrayPool<byte>.Shared.Return(_rented);
    }
}

internal static class WireInteger
{
    internal static int Size(int value) => BitOperations.Log2((uint)value) / 7 + 1;

    internal static int Write(Span<byte> bytes, int value)
    {
        int size = Size(value);
        for (int i = size - 1; i >= 0; i--)
        {
            bytes[i] = (byte)((value & 127) | (i == size - 1 ? 0 : 128));
            value >>= 7;
        }
        return size;
    }
}
