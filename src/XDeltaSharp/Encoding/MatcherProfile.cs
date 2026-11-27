namespace XDeltaSharp.Encoding;

/// <summary>Search settings for one compression level.</summary>
/// <param name="SourceStep">Distance in bytes between indexed source positions.</param>
/// <param name="TargetCandidates">Earlier target positions tried per probe. Zero turns target matching off.</param>
/// <param name="LazyCandidates">Earlier target positions tried when a match is already pending.</param>
/// <param name="LazyMatchLimit">Matches shorter than this get another look at the next position.</param>
/// <param name="TargetStopLength">A target match this long ends the search for its probe.</param>
internal readonly record struct MatcherProfile(int SourceStep, int TargetCandidates, int LazyCandidates,
    int LazyMatchLimit, int TargetStopLength)
{
    internal const int DefaultLevel = 3;
    internal const int MaxLevel = 9;
    internal const int SourceSeed = 9;
    internal const int TargetSeed = sizeof(uint);
    internal const int MinRunLength = 8;

    // Matcher parameters from xdelta3 3.2.2, xdelta3-cfgs.h and the CLI level mapping.
    internal static MatcherProfile FromLevel(int compressionLevel) => compressionLevel switch
    {
        0 => new(SourceStep: 26, TargetCandidates: 0, LazyCandidates: 0, LazyMatchLimit: 6, TargetStopLength: 6),
        1 => new(SourceStep: 26, TargetCandidates: 1, LazyCandidates: 1, LazyMatchLimit: 6, TargetStopLength: 6),
        2 => new(SourceStep: 15, TargetCandidates: 1, LazyCandidates: 1, LazyMatchLimit: 18, TargetStopLength: 18),
        >= 3 and <= 5 => new(SourceStep: 8, TargetCandidates: 4, LazyCandidates: 1, LazyMatchLimit: 18, TargetStopLength: 35),
        6 => new(SourceStep: 3, TargetCandidates: 8, LazyCandidates: 2, LazyMatchLimit: 36, TargetStopLength: 70),
        >= 7 and <= MaxLevel => new(SourceStep: 2, TargetCandidates: 44, LazyCandidates: 13, LazyMatchLimit: 90, TargetStopLength: 70),
        _ => throw new ArgumentOutOfRangeException(nameof(compressionLevel), "Choose a level from 0 to 9.")
    };
}
