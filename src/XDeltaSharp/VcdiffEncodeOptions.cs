using XDeltaSharp.Encoding;

namespace XDeltaSharp;

/// <summary>Settings for <see cref="Vcdiff"/> encoding. An instance can be reused across calls.</summary>
public sealed class VcdiffEncodeOptions
{
    internal static readonly VcdiffEncodeOptions Default = new();

    private readonly int _compressionLevel = MatcherProfile.DefaultLevel;
    private readonly MemoryMode _memoryMode;

    /// <summary>How hard the encoder searches for matches, from 0 (fastest) to 9 (smallest patch). The default
    /// is 3.</summary>
    /// <remarks>The levels use the matcher settings of the <c>xdelta3</c> command line, where 3 to 5 are the
    /// same setting and so are 7 to 9. Level 0 copies from the source only. Higher levels also copy from
    /// earlier target bytes.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 0 or greater than 9.</exception>
    public int CompressionLevel
    {
        get => _compressionLevel;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MatcherProfile.MaxLevel);
            _compressionLevel = value;
        }
    }

    /// <summary>How much memory the match indexes may use.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="MemoryMode"/>.</exception>
    public MemoryMode MemoryMode
    {
        get => _memoryMode;
        init
        {
            if (value is not (MemoryMode.Default or MemoryMode.LessMemory))
                throw new ArgumentOutOfRangeException(nameof(value));
            _memoryMode = value;
        }
    }

    /// <summary>Takes a second pass over each window to extend and merge its instructions, and keeps the result
    /// only when it is smaller.</summary>
    /// <remarks>The patch is never larger than without the pass. Encoding takes longer and uses temporary
    /// memory for one window's instructions.</remarks>
    public bool Optimize { get; init; }
}
