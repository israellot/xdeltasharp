namespace VCDiffSharp.Decoding;

internal ref struct WindowReader
{
    private SectionReader _file;
    internal readonly bool IsEmpty => _file.IsEmpty;

    internal WindowReader(ReadOnlySpan<byte> patch)
    {
        _file = new SectionReader(patch);
        if (_file.ReadByte() != 0xD6 || _file.ReadByte() != 0xC3 || _file.ReadByte() != 0xC4)
            throw new InvalidDataException("Invalid VCDIFF signature.");
        if (_file.ReadByte() != 0)
            throw new NotSupportedException("Only VCDIFF version 0 is supported.");

        byte flags = _file.ReadByte();
        if ((flags & ~7) != 0)
            throw new InvalidDataException("Invalid VCDIFF file header flags.");
        if ((flags & 1) != 0)
            throw new NotSupportedException("VCDIFF secondary compression is unsupported.");
        if ((flags & 2) != 0)
            throw new NotSupportedException("VCDIFF custom code tables are unsupported.");
        if ((flags & 4) != 0)
            throw new NotSupportedException("VCDIFF application headers are unsupported.");
    }

    internal Window ReadWindow(int sourceLength, int previousTargetLength)
    {
        byte flags = _file.ReadByte();
        if ((flags & ~7) != 0 || (flags & 3) == 3)
            throw new InvalidDataException("Invalid VCDIFF window indicator.");
        if ((flags & 4) != 0)
            throw new NotSupportedException("VCDIFF window checksums are unsupported.");

        bool usePriorTarget = flags == 2;
        int segmentLength = 0;
        int segmentOffset = 0;
        if (flags != 0)
        {
            segmentLength = _file.ReadLength();
            segmentOffset = _file.ReadLength();
            int available = usePriorTarget ? previousTargetLength : sourceLength;
            if (segmentLength > available || segmentOffset > available - segmentLength)
                throw new InvalidDataException("VCDIFF dictionary segment is outside the available data.");
        }

        int deltaLength = _file.ReadLength();
        var delta = new SectionReader(_file.Take(deltaLength));
        int targetLength = delta.ReadLength();
        if (delta.ReadByte() != 0)
            throw new InvalidDataException("Unexpected delta indicator in an uncompressed VCDIFF file.");

        int dataLength = delta.ReadLength();
        int instructionLength = delta.ReadLength();
        int addressLength = delta.ReadLength();
        var data = delta.Take(dataLength);
        var instructions = delta.Take(instructionLength);
        var addresses = delta.Take(addressLength);
        if (!delta.IsEmpty)
            throw new InvalidDataException("VCDIFF sections do not fill the declared delta length.");

        return new Window(usePriorTarget, segmentOffset, segmentLength, targetLength, data, instructions, addresses);
    }
}

internal readonly ref struct Window(bool usePriorTarget, int segmentOffset, int segmentLength,
    int targetLength, ReadOnlySpan<byte> data, ReadOnlySpan<byte> instructions, ReadOnlySpan<byte> addresses)
{
    internal bool UsePriorTarget { get; } = usePriorTarget;
    internal int SegmentOffset { get; } = segmentOffset;
    internal int SegmentLength { get; } = segmentLength;
    internal int TargetLength { get; } = targetLength;
    internal ReadOnlySpan<byte> Data { get; } = data;
    internal ReadOnlySpan<byte> Instructions { get; } = instructions;
    internal ReadOnlySpan<byte> Addresses { get; } = addresses;
}
