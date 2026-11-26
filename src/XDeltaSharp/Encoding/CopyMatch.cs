namespace XDeltaSharp.Encoding;

/// <summary>A COPY or RUN proposed for the current target window.</summary>
/// <param name="ReadOffset">Read position for a COPY, or <see cref="RunOffset"/> for a RUN. Target matcher positions
/// are relative to the current window until the parser adds the source segment length.</param>
/// <param name="WriteOffset">Target-window offset where the operation begins.</param>
/// <param name="Length">Number of matching bytes.</param>
internal readonly record struct CopyMatch(int ReadOffset, int WriteOffset, int Length)
{
    internal const int RunOffset = -1;
    internal bool IsRun => ReadOffset < 0;
    internal int End => WriteOffset + Length;
}
