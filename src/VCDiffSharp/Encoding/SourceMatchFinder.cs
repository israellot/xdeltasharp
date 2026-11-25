namespace VCDiffSharp.Encoding;

/// <summary>Indexes a fixed source segment for reuse by windows that select the same segment.</summary>
internal readonly ref struct SourceMatchFinder
{
    internal SourceMatchFinder(ReadOnlySpan<byte> sourceSegment)
        => throw new NotImplementedException();

    internal int SourceSegmentLength => throw new NotImplementedException();

    /// <summary>Finds a source match at the probe and may extend it backwards into pending literals.</summary>
    internal CopyMatch? FindMatch(ReadOnlySpan<byte> targetWindow, int probe, int pendingLiteralStart)
        => throw new NotImplementedException();

    /// <summary>Releases storage rented for the source index.</summary>
    public void Dispose() => throw new NotImplementedException();
}
