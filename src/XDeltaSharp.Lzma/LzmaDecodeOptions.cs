using XDeltaSharp.Decoding;

namespace XDeltaSharp.Lzma;

/// <summary>Limits and verification for <see cref="LzmaVcdiff"/> decoding. An instance can be reused across
/// calls.</summary>
/// <remarks>The data, instruction and address sections each have their own LZMA stream, so a decode can hold
/// three dictionaries and three expanded sections at once.</remarks>
public sealed class LzmaDecodeOptions : VcdiffDecodeOptions
{
    internal static new readonly LzmaDecodeOptions Default = new();

    private readonly int _maxExpandedSectionBytes = Vcdiff.MaxTargetWindowBytes;
    private readonly int _maxDictionaryBytes = Vcdiff.MaxTargetWindowBytes;

    /// <summary>The largest size one compressed section may expand to, in bytes. The default is 64 MiB.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/>.</exception>
    public int MaxExpandedSectionBytes
    {
        get => _maxExpandedSectionBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Vcdiff.MaxTargetWindowBytes);
            _maxExpandedSectionBytes = value;
        }
    }

    /// <summary>The largest LZMA dictionary a section stream may declare, in bytes. The default is 64 MiB, the
    /// largest xdelta3 preset.</summary>
    /// <remarks>The declared size is checked before the stream is opened.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/>.</exception>
    public int MaxDictionaryBytes
    {
        get => _maxDictionaryBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Vcdiff.MaxTargetWindowBytes);
            _maxDictionaryBytes = value;
        }
    }

    internal SecondaryDecoderFactory Sections => new(
        static (section, dictionary) => new LzmaSections(section, dictionary), _maxExpandedSectionBytes, _maxDictionaryBytes);
}
