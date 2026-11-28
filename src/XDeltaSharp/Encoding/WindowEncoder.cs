using System.Buffers;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

internal static class WindowEncoder
{
    internal const int TargetWindowSize = 8 << 20;
    private const int SourceSegmentSize = 64 << 20;

    // Stack scratch for one window's sections and pending matches. Larger windows spill to pooled arrays.
    internal const int DataScratchSize = 128;
    internal const int InstructionScratchSize = 64;
    internal const int AddressScratchSize = 64;
    private const int PendingMatchScratch = 16;
    // Shorter windows are encoded without the SAME address cache.
    private const int MinTargetForSameCache = 128;

    internal static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeOptions options)
    {
        var profile = MatcherProfile.FromLevel(options.CompressionLevel);
        // A single window can allocate its exact result. Longer targets need to collect multiple frames.
        if (target.Length <= TargetWindowSize)
            return EncodeWindows(source, target, null, options.MemoryMode, profile, options.Optimize)!;

        var destination = new ArrayBufferWriter<byte>();
        EncodeWindows(source, target, destination, options.MemoryMode, profile, options.Optimize);
        return destination.WrittenSpan.ToArray();
    }

    internal static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeOptions options)
        => EncodeWindows(source, target, destination, options.MemoryMode,
            MatcherProfile.FromLevel(options.CompressionLevel), options.Optimize);

    private static byte[]? EncodeWindows(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte>? destination,
        MemoryMode memoryMode, MatcherProfile profile, bool optimize)
    {
        int start = 0;
        int segmentLength = Math.Min(source.Length, SourceSegmentSize);
        int scratchSize = LevelSourceMatcher.ScratchSize(segmentLength, target.Length, profile.SourceStep, memoryMode);
        Span<byte> scratch = stackalloc byte[scratchSize];
        do
        {
            int segmentStart = SegmentStart(source.Length, start);
            var segment = source.Slice(segmentStart, segmentLength);
            using var matcher = new LevelSourceMatcher(segment, target.Length - start, profile.SourceStep, memoryMode, scratch);
            do
            {
                int length = Math.Min(target.Length - start, TargetWindowSize);
                var window = target.Slice(start, length);
                byte[]? result = EncodeWindow(in matcher, segment, segmentStart, window, start,
                    destination, memoryMode, profile, optimize);
                if (destination is null) return result;
                start += length;
            } while (start < target.Length && SegmentStart(source.Length, start) == segmentStart);
        } while (start < target.Length);
        return null;
    }

    private static int SegmentStart(int sourceLength, int targetOffset)
        => Math.Min(targetOffset / SourceSegmentSize * SourceSegmentSize, Math.Max(0, sourceLength - SourceSegmentSize));

    private static byte[]? EncodeWindow(in LevelSourceMatcher matcher, ReadOnlySpan<byte> segment, int segmentOffset,
        ReadOnlySpan<byte> target, int targetOffset, IBufferWriter<byte>? destination,
        MemoryMode memoryMode, MatcherProfile profile, bool optimize)
    {
        Span<int> same = target.Length >= MinTargetForSameCache ? stackalloc int[AddressCache.SameSize] : [];
        var writer = new WindowWriter(segmentOffset, segment.Length, stackalloc byte[DataScratchSize],
            stackalloc byte[InstructionScratchSize], stackalloc byte[AddressScratchSize], same);
        try
        {
            LevelParser.Parse(in matcher, target, targetOffset - segmentOffset, profile, memoryMode,
                stackalloc CopyMatch[PendingMatchScratch], ref writer);
            if (optimize)
                return WindowOptimizer.FinishSmallest(ref writer, segment, segmentOffset, target, targetOffset == 0, destination);
            return writer.Finish(target, targetOffset == 0, destination);
        }
        finally
        {
            writer.Dispose();
        }
    }
}
