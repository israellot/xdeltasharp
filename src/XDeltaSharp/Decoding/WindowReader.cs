using XDeltaSharp.Format;

namespace XDeltaSharp.Decoding;

/// <summary>Reads the same file and window framing from contiguous memory or a sequential stream.</summary>
internal ref struct WindowReader
{
    private ReadOnlySpan<byte> _remaining;
    private readonly Stream? _stream;
    private readonly int _windowLimit;
    private readonly int _encodedWindowLimit;
    private byte[] _sectionBuffer;
    private long _headerBytes;
    private bool _hasSecondary;

    internal WindowReader(ReadOnlySpan<byte> patch, int windowLimit, bool allowLzma = false, IArmorVerifier? armor = null)
    {
        _remaining = patch;
        _encodedWindowLimit = Array.MaxLength;
        _sectionBuffer = [];
        _windowLimit = windowLimit;
        ReadFileHeader(allowLzma, armor);
    }

    internal WindowReader(Stream patch, int windowLimit, int maxEncodedWindowBytes, bool allowLzma = false,
        IArmorVerifier? armor = null)
    {
        _stream = patch;
        _encodedWindowLimit = maxEncodedWindowBytes;
        _sectionBuffer = [];
        _windowLimit = windowLimit;
        ReadFileHeader(allowLzma, armor);
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
            if (!WireInteger.TryAppendDigit(ref value, digit))
                throw new InvalidDataException("VCDIFF header integer exceeds the supported range.");
        } while ((digit & WireInteger.ContinuationBit) != 0);
        return value;
    }

    private int ReadSectionLength()
    {
        long value = ReadInteger();
        if (value > Array.MaxLength)
            throw new InvalidDataException("VCDIFF section exceeds the supported buffer size.");
        return (int)value;
    }

    private void ReadFileHeader(bool allowLzma, IArmorVerifier? armor)
    {
        if (ReadByte() != FileHeader.SignatureByte1 || ReadByte() != FileHeader.SignatureByte2 ||
            ReadByte() != FileHeader.SignatureByte3)
            throw new InvalidDataException("Invalid VCDIFF signature.");
        if (ReadByte() != FileHeader.Version)
            throw new NotSupportedException("Only VCDIFF version 0 is supported.");

        var flags = (FileHeaderFlags)ReadByte();
        if ((flags & ~FileHeaderFlags.KnownMask) != 0)
            throw new InvalidDataException("Invalid VCDIFF file header flags.");
        if ((flags & FileHeaderFlags.SecondaryCompression) != 0)
        {
            byte compressor = ReadByte();
            if (compressor != FileHeader.XdeltaLzmaCompressorId)
                throw new NotSupportedException($"Unsupported VCDIFF secondary compressor ID {compressor}.");
            if (!allowLzma)
                throw new NotSupportedException("Use the xdelta.Lzma package to decode xdelta3 LZMA secondary compression.");
            _hasSecondary = true;
        }
        if ((flags & FileHeaderFlags.CustomCodeTable) != 0)
            throw new NotSupportedException("VCDIFF custom code tables are unsupported.");
        if ((flags & FileHeaderFlags.ApplicationHeader) != 0)
            ReadApplicationHeader(ReadInteger(), armor);
    }

    private void ReadApplicationHeader(long length, IArmorVerifier? armor)
    {
        if (_stream is null)
        {
            if (length > _remaining.Length)
                throw new InvalidDataException("Truncated VCDIFF application header.");
            armor?.ReadHeader(_remaining[..(int)length]);
            _remaining = _remaining[(int)length..];
            return;
        }

        Span<byte> scratch = stackalloc byte[1024];
        while (length > 0)
        {
            int count = _stream.Read(scratch[..(int)Math.Min(length, scratch.Length)]);
            if (count == 0)
                throw new InvalidDataException("Truncated VCDIFF application header.");
            armor?.ReadHeader(scratch[..count]);
            length -= count;
        }
    }

    internal bool TryReadWindow(long sourceLength, long previousTargetLength, long outputRemaining, out Window window)
    {
        window = default;
        int indicator = NextByte();
        if (indicator == -1)
            return false;
        var flags = (WindowFlags)indicator;
        var dictionaryFlags = flags & WindowFlags.DictionaryMask;
        if ((flags & ~WindowFlags.KnownMask) != 0 || dictionaryFlags == WindowFlags.DictionaryMask)
            throw new InvalidDataException("Invalid VCDIFF window indicator.");
        bool usePriorTarget = dictionaryFlags == WindowFlags.PriorTarget;
        long segmentLength = 0;
        long segmentOffset = 0;
        if (dictionaryFlags != WindowFlags.None)
        {
            segmentLength = ReadInteger();
            segmentOffset = ReadInteger();
            long available = usePriorTarget ? previousTargetLength : sourceLength;
            if (available < 0)
            {
                throw new NotSupportedException(usePriorTarget
                    ? "Prior-target dictionaries require a readable, seekable destination."
                    : "Source dictionaries require a readable, seekable source.");
            }
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
        byte compressedSections = ReadByte();
        const byte sectionMask = (byte)(SectionKind.Data | SectionKind.Instructions | SectionKind.Addresses);
        if ((compressedSections & ~sectionMask) != 0 || (compressedSections != 0 && !_hasSecondary))
            throw new InvalidDataException("Invalid VCDIFF section compression flags.");

        int dataLength = ReadSectionLength();
        int instructionLength = ReadSectionLength();
        int addressLength = ReadSectionLength();
        uint? checksum = null;
        if ((flags & WindowFlags.Adler32) != 0)
        {
            uint value = 0;
            for (int i = 0; i < sizeof(uint); i++)
                value = (value << 8) | ReadByte();
            checksum = value;
        }
        long payloadLength = (long)dataLength + instructionLength + addressLength;
        if (deltaLength - _headerBytes != payloadLength)
            throw new InvalidDataException("VCDIFF section lengths do not match the declared delta length.");
        if (payloadLength > _encodedWindowLimit)
            throw new InvalidDataException($"VCDIFF encoded sections exceed the {_encodedWindowLimit}-byte window payload limit.");

        var payload = ReadPayload((int)payloadLength);
        window = new Window(usePriorTarget, segmentOffset, segmentLength, (int)targetLength, checksum, compressedSections,
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
    int targetLength, uint? checksum, byte compressedSections,
    ReadOnlySpan<byte> data, ReadOnlySpan<byte> instructions, ReadOnlySpan<byte> addresses)
{
    internal bool UsePriorTarget { get; } = usePriorTarget;
    internal long SegmentOffset { get; } = segmentOffset;
    internal long SegmentLength { get; } = segmentLength;
    internal int TargetLength { get; } = targetLength;
    internal uint? Checksum { get; } = checksum;
    internal byte CompressedSections { get; } = compressedSections;
    internal ReadOnlySpan<byte> Data { get; } = data;
    internal ReadOnlySpan<byte> Instructions { get; } = instructions;
    internal ReadOnlySpan<byte> Addresses { get; } = addresses;
}
