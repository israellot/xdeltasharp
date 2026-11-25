namespace VCDiffSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
public enum VcdiffEncodeMode
{
    /// <summary>Use the level-3 matcher.</summary>
    Default = 0,

    /// <summary>Search up to 256 source collision links and use selective one-byte lookahead for related inputs.</summary>
    Fast = 1,

    /// <summary>Search full source and target chains by estimated encoding savings.</summary>
    Exhaustive = 2
}
