namespace XDeltaSharp.Encoding;

// Matcher parameters from xdelta3 3.2.2, xdelta3-cfgs.h and the CLI level mapping.
internal readonly record struct MatcherProfile(int SourceStep, int TargetChain, int LazyChain, int MaxLazy, int LongEnough)
{
    internal const int DefaultLevel = 3;
    internal const int SourceSeed = 9;
    internal const int TargetSeed = 4;

    internal static MatcherProfile FromLevel(int compressionLevel) => compressionLevel switch
    {
        0 => new(26, 0, 0, 6, 6),
        1 => new(26, 1, 1, 6, 6),
        2 => new(15, 1, 1, 18, 18),
        >= 3 and <= 5 => new(8, 4, 1, 18, 35),
        6 => new(3, 8, 2, 36, 70),
        >= 7 and <= 9 => new(2, 44, 13, 90, 70),
        _ => throw new ArgumentOutOfRangeException(nameof(compressionLevel), "Choose a level from 0 to 9.")
    };
}
