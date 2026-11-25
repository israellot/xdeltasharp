using System.Buffers;

namespace VCDiffSharp.Encoding;

/// <summary>Chooses ADD, RUN, or COPY and writes one complete window.</summary>
/// <remarks>Borrows the source finder and creates a fresh TargetMatchFinder for this window.</remarks>
internal static class WindowEncoder
{
    internal static void Encode(in SourceMatchFinder sourceMatches, long sourceOffset,
        ReadOnlySpan<byte> targetWindow, IBufferWriter<byte> destination)
        => throw new NotImplementedException();
}
