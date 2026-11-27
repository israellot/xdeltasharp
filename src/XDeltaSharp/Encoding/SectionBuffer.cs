using System.Buffers;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

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
        if (value < WireInteger.FirstTwoByteValue)
        {
            Add((byte)value);
            return;
        }
        if (_buffer.Length - Length < WireInteger.MaxInt32Size) Grow(WireInteger.MaxInt32Size);
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
