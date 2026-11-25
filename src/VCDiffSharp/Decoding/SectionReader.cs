namespace VCDiffSharp.Decoding;

/// <summary>A cursor bounded to the current VCDIFF file, window, or section.</summary>
internal ref struct SectionReader(ReadOnlySpan<byte> bytes)
{
    private ReadOnlySpan<byte> _remaining = bytes;
    internal readonly bool IsEmpty => _remaining.IsEmpty;

    internal byte ReadByte()
    {
        if (_remaining.IsEmpty)
            throw new InvalidDataException("Unexpected end of VCDIFF data.");

        byte value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    internal uint ReadInteger()
    {
        uint value = 0;
        byte digit;
        do
        {
            digit = ReadByte();
            if (value > uint.MaxValue >> 7)
                throw new InvalidDataException("VCDIFF integer exceeds the supported range.");

            value = (value << 7) | (uint)(digit & 0x7F);
        } while ((digit & 0x80) != 0);
        return value;
    }

    internal int ReadLength()
    {
        uint length = ReadInteger();
        if (length > int.MaxValue)
            throw new InvalidDataException("VCDIFF length or offset exceeds the supported span size.");
        return (int)length;
    }

    internal ReadOnlySpan<byte> Take(int length)
    {
        if (length > _remaining.Length)
            throw new InvalidDataException("VCDIFF section extends beyond its declared bounds.");

        var result = _remaining[..length];
        _remaining = _remaining[length..];
        return result;
    }
}
