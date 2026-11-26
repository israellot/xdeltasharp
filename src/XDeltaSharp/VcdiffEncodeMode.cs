namespace XDeltaSharp;

/// <summary>Controls how the encoder searches for matches.</summary>
/// <remarks>All modes write the same patch format. The decoder obtains the instructions it needs from the patch.</remarks>
public enum VcdiffEncodeMode
{
    /// <summary>Use level 3 to search the source dictionary and earlier bytes in the current target window.</summary>
    Default = 0,

    /// <summary>Use a bounded source-only match search. Useful for quickly patching small changes to a related file.</summary>
    Fast = 1,

    /// <summary>Search full source and target candidate chains, ranking matches by estimated patch size savings.</summary>
    Exhaustive = 2
}
