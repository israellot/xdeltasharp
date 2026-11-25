namespace VCDiffSharp.Decoding;

/// <summary>Reads the same file and window framing from contiguous memory or a sequential stream.</summary>
internal ref struct WindowReader
{
    private ReadOnlySpan<byte> _remaining;
    private readonly Stream? _stream;
    private readonly int _windowLimit;
    private byte[] _sectionBuffer;
    private long _headerBytes;

    internal WindowReader(ReadOnlySpan<byte> patch, int windowLimit)
    {
        _remaining = patch;
        _sectionBuffer = [];
        _windowLimit = windowLimit;
        ReadFileHeader();
    }

    internal WindowReader(Stream patch, int windowLimit)
    {
        _stream = patch;
        _sectionBuffer = [];
        _windowLimit = windowLimit;
        ReadFileHeader();
    }

    private int NextByte()
    {
        if (_stream != null)
            return _stream.ReadByte();
        if (_remaining.IsEmpty)
            return -1;
        int value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    private byte ReadByte()
    {
        int value = NextByte();
        if (value < 0)
            throw new InvalidDataException("Unexpected end of VCDIFF header.");
        _headerBytes++;
        return (byte)value;
    }

    private long ReadInteger()
    {
        long value = 0;
        byte digit;
        do
        {
            digit = ReadByte();
            if (value > long.MaxValue >> 7)
                throw new InvalidDataException("VCDIFF header integer exceeds the supported range.");
            value = (value << 7) | (uint)(digit & 127);
        } while ((digit & 128) != 0);
        return value;
    }

    private int ReadSectionLength()
    {
        long value = ReadInteger();
        if (value > Array.MaxLength)
            throw new InvalidDataException("VCDIFF section exceeds the supported buffer size.");
        return (int)value;
    }

    private void ReadFileHeader()
    {
        if (ReadByte() != 0xD6 || ReadByte() != 0xC3 || ReadByte() != 0xC4)
            throw new InvalidDataException("Invalid VCDIFF signature.");
        if (ReadByte() != 0)
            throw new NotSupportedException("Only VCDIFF version 0 is supported.");

        byte flags = ReadByte();
        if ((flags & ~7) != 0)
            throw new InvalidDataException("Invalid VCDIFF file header flags.");
        if ((flags & 1) != 0)
            throw new NotSupportedException("VCDIFF secondary compression is unsupported.");
        if ((flags & 2) != 0)
            throw new NotSupportedException("VCDIFF custom code tables are unsupported.");
        if ((flags & 4) != 0)
            throw new NotSupportedException("VCDIFF application headers are unsupported.");
    }

    internal bool TryReadWindow(long sourceLength, long previousTargetLength, long outputRemaining, out Window window)
    {
        window = default;
        int flags = NextByte();
        if (flags == -1)
            return false;
        if ((flags & ~7) != 0 || (flags & 3) == 3)
            throw new InvalidDataException("Invalid VCDIFF window indicator.");
        if ((flags & 4) != 0)
            throw new NotSupportedException("VCDIFF window checksums are unsupported.");

        bool usePriorTarget = flags == 2;
        long segmentLength = 0;
        long segmentOffset = 0;
        if (flags != 0)
        {
            segmentLength = ReadInteger();
            segmentOffset = ReadInteger();
            long available = usePriorTarget ? previousTargetLength : sourceLength;
            if (available < 0)
                throw new NotSupportedException("Source dictionaries require a readable, seekable source.");
            if (segmentLength > available || segmentOffset > available - segmentLength)
                throw new InvalidDataException("VCDIFF dictionary segment is outside the available data.");
        }

        long deltaLength = ReadInteger();
        _headerBytes = 0;
        long targetLength = ReadInteger();
        if (targetLength > _windowLimit)
            throw new InvalidDataException($"VCDIFF target window exceeds the {_windowLimit}-byte window limit.");
        if (targetLength > outputRemaining)
            throw new InvalidDataException("The target exceeds the output limit.");
        if (segmentLength > long.MaxValue - targetLength)
            throw new InvalidDataException("VCDIFF dictionary and window exceed the supported address range.");
        if (ReadByte() != 0)
            throw new InvalidDataException("Unexpected delta indicator in an uncompressed VCDIFF file.");

        int dataLength = ReadSectionLength();
        int instructionLength = ReadSectionLength();
        int addressLength = ReadSectionLength();
        long payloadLength = (long)dataLength + instructionLength + addressLength;
        if (deltaLength - _headerBytes != payloadLength || payloadLength > Array.MaxLength)
            throw new InvalidDataException("VCDIFF sections do not match the declared delta length or buffer capacity.");

        var payload = ReadPayload((int)payloadLength);
        window = new Window(usePriorTarget, segmentOffset, segmentLength, (int)targetLength,
            payload[..dataLength], payload.Slice(dataLength, instructionLength), payload[(dataLength + instructionLength)..]);
        return true;
    }

    private ReadOnlySpan<byte> ReadPayload(int length)
    {
        if (_stream is null)
        {
            if (length > _remaining.Length)
                throw new InvalidDataException("Truncated VCDIFF window.");
            var payload = _remaining[..length];
            _remaining = _remaining[length..];
            return payload;
        }

        // Grow with actual input so a truncated patch cannot force a large allocation from its header alone.
        int used = 0;
        while (used < length)
        {
            if (used == _sectionBuffer.Length)
                Array.Resize(ref _sectionBuffer, (int)Math.Min(length, Math.Max(4096L, 2L * used)));
            int count = _stream.Read(_sectionBuffer.AsSpan(used, Math.Min(_sectionBuffer.Length, length) - used));
            if (count == 0)
                throw new InvalidDataException("Truncated VCDIFF window.");
            used += count;
        }
        return _sectionBuffer.AsSpan(0, length);
    }
}

internal readonly ref struct Window(bool usePriorTarget, long segmentOffset, long segmentLength,
    int targetLength, ReadOnlySpan<byte> data, ReadOnlySpan<byte> instructions, ReadOnlySpan<byte> addresses)
{
    internal bool UsePriorTarget { get; } = usePriorTarget;
    internal long SegmentOffset { get; } = segmentOffset;
    internal long SegmentLength { get; } = segmentLength;
    internal int TargetLength { get; } = targetLength;
    internal ReadOnlySpan<byte> Data { get; } = data;
    internal ReadOnlySpan<byte> Instructions { get; } = instructions;
    internal ReadOnlySpan<byte> Addresses { get; } = addresses;
}
