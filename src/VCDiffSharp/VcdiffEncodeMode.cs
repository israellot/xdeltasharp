namespace VCDiffSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
public enum VcdiffEncodeMode
{
    /// <summary>Use the level-3 matcher.</summary>
    Default = 0,

    /// <summary>Search the source bucket head and up to 256 collision links per position and encode literal data and runs.</summary>
    Fast = 1,

    /// <summary>Search full source and target chains by estimated encoding savings.</summary>
    Exhaustive = 2
}
