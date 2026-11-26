using System.Buffers.Binary;
using SharpCompress.Common;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.Xz;
using XDeltaSharp.Decoding;

namespace XDeltaSharp.Lzma;

internal sealed class LzmaSections(int maxExpandedSectionBytes, int maxDictionaryBytes) : ISecondaryDecoder
{
    private SectionStream? _data, _instructions, _addresses;

    internal static SecondaryDecoderFactory Default { get; } = WithLimits(Vcdiff.MaxTargetWindowBytes, Vcdiff.MaxTargetWindowBytes);

    internal static SecondaryDecoderFactory WithLimits(int maxExpandedSectionBytes, int maxDictionaryBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedSectionBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxExpandedSectionBytes, Vcdiff.MaxTargetWindowBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDictionaryBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDictionaryBytes, Vcdiff.MaxTargetWindowBytes);
        return new(static (section, dictionary) => new LzmaSections(section, dictionary), maxExpandedSectionBytes, maxDictionaryBytes);
    }

    public ReadOnlySpan<byte> Decode(SectionKind kind, ReadOnlySpan<byte> encoded)
    {
        var prefix = new SectionReader(encoded);
        int length = prefix.ReadLength();
        if (length == 0 || length > maxExpandedSectionBytes)
            throw new InvalidDataException("Expanded xdelta3 section length is zero or exceeds its limit.");

        var section = kind switch
        {
            SectionKind.Data => _data ??= new(),
            SectionKind.Instructions => _instructions ??= new(),
            SectionKind.Addresses => _addresses ??= new(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return section.Decode(prefix.Remaining, length, maxDictionaryBytes);
    }

    public void Dispose()
    {
        _data?.Dispose();
        _instructions?.Dispose();
        _addresses?.Dispose();
    }

    // Each section kind has its own sync-flushed XZ block that continues across windows.
    private sealed class SectionStream : IDisposable
    {
        private readonly MemoryStream _input = new();
        private LzmaStream? _decoder;
        private byte[] _output = [];

        internal ReadOnlySpan<byte> Decode(ReadOnlySpan<byte> encoded, int length, int maxDictionaryBytes)
        {
            try
            {
                if (_decoder == null)
                {
                    byte property = ReadXdeltaHeader(encoded, maxDictionaryBytes);
                    _decoder = LzmaStream.Create([property], _input, leaveOpen: true);
                    encoded = encoded[24..];
                }

                _input.SetLength(0);
                _input.Write(encoded);
                _input.Position = 0;
                int written = 0;
                while (written < length)
                {
                    if (written == _output.Length)
                        Array.Resize(ref _output, Math.Min(length, Math.Max(4096, written * 2)));
                    int count = _decoder.Read(_output, written, Math.Min(length, _output.Length) - written);
                    if (count == 0)
                        throw new InvalidDataException("Truncated xdelta3 LZMA section.");
                    written += count;
                }

                // A sync flush ends at an LZMA2 chunk boundary, with no input or output pending.
                if (_input.Position != _input.Length || _decoder.Position != _decoder.Length)
                    throw new InvalidDataException("xdelta3 LZMA section length mismatch.");
                return _output.AsSpan(0, length);
            }
            catch (Exception error) when (error is SharpCompressException or EndOfStreamException)
            {
                throw new InvalidDataException("Invalid xdelta3 LZMA section.", error);
            }
        }

        private static byte ReadXdeltaHeader(ReadOnlySpan<byte> bytes, int maxDictionaryBytes)
        {
            // liblzma emits a no-check XZ stream followed by a single LZMA2 filter.
            // The block has no size fields. Only the dictionary property varies by preset.
            ReadOnlySpan<byte> header = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0, 0, 0, 0xFF, 0x12, 0xD9, 0x41,
                2, 0, 0x21, 1];
            if (bytes.Length < 24 || !bytes.StartsWith(header) || bytes[17..20].IndexOfAnyExcept((byte)0) >= 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) != ~Crc32.Update(Crc32.DefaultSeed, bytes[12..20]))
                throw new InvalidDataException("Invalid xdelta3 LZMA stream or block header.");
            byte property = bytes[16];
            // LZMA2 dictionaries alternate between powers of two and 1.5 times those sizes.
            // Property 28 represents 64 MiB, the largest xdelta3 preset.
            if (property > 28 || ((2 | (property & 1)) << (11 + (property >> 1))) > maxDictionaryBytes)
                throw new InvalidDataException("Declared xdelta3 LZMA dictionary exceeds its limit.");
            return property;
        }

        public void Dispose()
        {
            _decoder?.Dispose();
            _input.Dispose();
        }
    }
}
