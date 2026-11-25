namespace VCDiffSharp.Encoding;

/// <summary>Indexes earlier match starts within one target window.</summary>
internal ref struct TargetMatchFinder
{
    internal TargetMatchFinder(ReadOnlySpan<byte> targetWindow)
        => throw new NotImplementedException();

    /// <summary>Advances the index to this probe and finds a match starting strictly before it.</summary>
    /// <remarks>Probes advance monotonically. A match can extend beyond the probe through overlap.</remarks>
    internal CopyMatch? FindMatch(int probe)
        => throw new NotImplementedException();
}
