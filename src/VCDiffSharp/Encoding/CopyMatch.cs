namespace VCDiffSharp.Encoding;

/// <summary>A candidate COPY found while scanning the current target window.</summary>
/// <param name="ReadOffset">Offset in the source segment for a source match, or in the current window for a target match.</param>
/// <param name="WriteOffset">Target-window offset where the COPY would begin.</param>
/// <param name="Length">Number of matching bytes.</param>
internal readonly record struct CopyMatch(int ReadOffset, int WriteOffset, int Length)
{
    internal int End => WriteOffset + Length;
}
