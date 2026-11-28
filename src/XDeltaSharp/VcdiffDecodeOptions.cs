using XDeltaSharp.Decoding;

namespace XDeltaSharp;

/// <summary>Limits and verification for <see cref="Vcdiff"/> decoding. An instance can be reused across
/// calls.</summary>
/// <remarks>A patch states its own sizes, so set limits that fit the files you expect before decoding a patch
/// you did not create.</remarks>
public class VcdiffDecodeOptions
{
    internal static readonly VcdiffDecodeOptions Default = new();

    private readonly long _maxOutputBytes = long.MaxValue;
    private readonly int _maxWindowBytes = Vcdiff.MaxTargetWindowBytes;
    private readonly int _maxEncodedWindowBytes = Array.MaxLength;

    /// <summary>The largest target the decoder will produce, in bytes. The default is unlimited.</summary>
    /// <remarks>A returned array is also limited to <see cref="Array.MaxLength"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MaxOutputBytes
    {
        get => _maxOutputBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxOutputBytes = value;
        }
    }

    /// <summary>The largest target window the decoder will accept, in bytes. The default is
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/>.</summary>
    /// <remarks>Stream decoding buffers one decoded target window at a time, and this bounds that buffer. The
    /// window's encoded sections are buffered separately, under <see cref="MaxEncodedWindowBytes"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/>.</exception>
    public int MaxWindowBytes
    {
        get => _maxWindowBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Vcdiff.MaxTargetWindowBytes);
            _maxWindowBytes = value;
        }
    }

    /// <summary>The largest encoded window the stream decoder will buffer, in bytes. The default is
    /// <see cref="Array.MaxLength"/>.</summary>
    /// <remarks>This counts a window's data, instruction and address sections and is checked before they are
    /// read. It has no effect when the patch is already in memory.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than
    /// <see cref="Array.MaxLength"/>.</exception>
    public int MaxEncodedWindowBytes
    {
        get => _maxEncodedWindowBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Array.MaxLength);
            _maxEncodedWindowBytes = value;
        }
    }

    /// <summary>Verifies the whole-file digests in the patch's application header. The default skips them.</summary>
    /// <remarks>Use <c>Blake3Armor.Verify</c> or <c>Blake3Armor.Require</c> from the <c>xdelta.Blake3</c>
    /// package. A source digest is checked before any output is written and a target digest after the last
    /// window.</remarks>
    public VcdiffArmor Armor { get; init; }

    internal DecodeLimits Limits => new(_maxOutputBytes, _maxWindowBytes, _maxEncodedWindowBytes);
}
