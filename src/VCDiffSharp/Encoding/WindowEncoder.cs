using System.Buffers;

namespace VCDiffSharp.Encoding;

/// <summary>Selects source matches, target matches, and runs, then writes one complete window.</summary>
internal static class WindowEncoder
{
    internal static void Encode(ReadOnlySpan<byte> sourceSegment, long sourceOffset,
        ReadOnlySpan<byte> targetWindow, IBufferWriter<byte> destination)
        => throw new NotImplementedException();
}
