namespace VCDiffSharp.Decoding;

/// <summary>Reconstructs one target window from its selected dictionary and three instruction-related sections.</summary>
internal static class WindowDecoder
{
    internal static void Decode(ReadOnlySpan<byte> sourceSegment, ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> instructions, ReadOnlySpan<byte> addresses, Span<byte> targetWindow)
        => throw new NotImplementedException();
}
