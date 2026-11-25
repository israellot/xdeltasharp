namespace VCDiffSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
public enum VcdiffEncodeMode
{
    /// <summary>Probe one source candidate per position and encode literal data and runs.</summary>
    Fast = 1
}
