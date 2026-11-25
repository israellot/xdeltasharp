namespace VCDiffSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
public enum VcdiffEncodeMode
{
    /// <summary>Search the source bucket head and up to 256 collision links per position and encode literal data and runs.</summary>
    Fast = 1
}
