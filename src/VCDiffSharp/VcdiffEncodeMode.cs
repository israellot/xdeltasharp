namespace VCDiffSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
public enum VcdiffEncodeMode
{
    /// <summary>Search source landmarks and earlier target positions by estimated encoding savings.</summary>
    Default = 0,

    /// <summary>Search the source bucket head and up to 256 collision links per position and encode literal data and runs.</summary>
    Fast = 1
}
