using System.Buffers;

namespace VCDiffSharp.Encoding;

internal static class WindowEncoder
{
    internal const int TargetWindowSize = 8 << 20;
    private const int SourceSegmentSize = 64 << 20;

    internal static byte[]? Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte>? destination,
        VcdiffEncodeMode mode, MemoryMode memoryMode, int compressionLevel = MatcherProfile.DefaultLevel)
    {
        var profile = MatcherProfile.FromLevel(compressionLevel);
        // A single window knows its final size before writing. Only multi-window array results need aggregation.
        ArrayBufferWriter<byte>? collected = null;
        if (destination == null && target.Length > TargetWindowSize)
            destination = collected = new ArrayBufferWriter<byte>();

        int start = 0;
        int segmentLength = Math.Min(source.Length, SourceSegmentSize);
        int smallSize = mode == VcdiffEncodeMode.Default
            ? LevelSourceMatcher.ScratchSize(segmentLength, target.Length, profile.SourceStep, memoryMode)
            : SourceMatchFinder.SmallTableSize(segmentLength, target.Length, memoryMode);
        Span<byte> smallTable = stackalloc byte[smallSize];
        do
        {
            int segmentStart = SegmentStart(source.Length, start);
            var segment = source.Slice(segmentStart, segmentLength);
            using var finder = mode == VcdiffEncodeMode.Default ? default :
                new SourceMatchFinder(segment, target.Length - start, memoryMode, smallTable, mode);
            using var levelSource = mode == VcdiffEncodeMode.Default
                ? new LevelSourceMatcher(segment, target.Length - start, profile.SourceStep, memoryMode, smallTable) : default;
            do
            {
                int length = Math.Min(target.Length - start, TargetWindowSize);
                byte[]? result = EncodeWindow(in finder, in levelSource, segmentStart, target.Slice(start, length), start,
                    destination, mode, memoryMode, profile);
                if (destination == null) return result;
                start += length;
            } while (start < target.Length && SegmentStart(source.Length, start) == segmentStart);
        } while (start < target.Length);
        return collected?.WrittenSpan.ToArray();
    }

    private static int SegmentStart(int sourceLength, int targetOffset)
        => Math.Min(targetOffset / SourceSegmentSize * SourceSegmentSize, Math.Max(0, sourceLength - SourceSegmentSize));

    private static byte[]? EncodeWindow(in SourceMatchFinder finder, in LevelSourceMatcher levelSource, int segmentOffset,
        ReadOnlySpan<byte> target, int targetOffset, IBufferWriter<byte>? destination, VcdiffEncodeMode mode,
        MemoryMode memoryMode, MatcherProfile profile)
    {
        Span<int> same = target.Length >= 128 && (mode != VcdiffEncodeMode.Fast || finder.SourceSegmentLength >= 128) ? stackalloc int[768] : [];
        var writer = new WindowWriter(segmentOffset, mode == VcdiffEncodeMode.Default ? levelSource.Length : finder.SourceSegmentLength,
            stackalloc byte[128], stackalloc byte[64], stackalloc byte[64], same);
        try
        {
            if (mode == VcdiffEncodeMode.Fast)
                ParseFast(in finder, target, ref writer);
            else if (mode == VcdiffEncodeMode.Default)
                LevelParser.Parse(in levelSource, target, targetOffset - segmentOffset, profile, memoryMode, stackalloc CopyMatch[16], ref writer);
            else
                ParseExhaustive(in finder, target, memoryMode, ref writer);
            return writer.Finish(target, targetOffset == 0, destination);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void ParseFast(scoped in SourceMatchFinder finder, ReadOnlySpan<byte> target, ref WindowWriter writer)
    {
        int pendingStart = 0;
        int probe = 0;
        while (probe <= target.Length - 3)
        {
            var match = finder.FindMatch(target, probe, pendingStart);
            int run = RunLength(target[probe..]);
            if (run != 0 && run >= match.Length)
            {
                writer.Add(target[pendingStart..probe]);
                writer.Run(target[probe], run);
                probe += run;
            }
            else if (match.Length >= 4)
            {
                writer.Add(target[pendingStart..match.WriteOffset]);
                writer.Copy(match.ReadOffset, match.Length);
                probe = match.WriteOffset + match.Length;
            }
            else
            {
                probe++;
                continue;
            }
            pendingStart = probe;
        }
        writer.Add(target[pendingStart..]);
    }

    private static void ParseExhaustive(scoped in SourceMatchFinder source, ReadOnlySpan<byte> target,
        MemoryMode memoryMode, ref WindowWriter writer)
    {
        Span<byte> scratch = stackalloc byte[TargetMatchFinder.SmallTableSize(target.Length, memoryMode)];
        var history = new TargetMatchFinder(target, scratch, memoryMode);
        try
        {
            int pendingStart = 0;
            int probe = 0;
            while (probe <= target.Length - 3)
            {
                WindowChoice best = default;
                int run = RunLength(target[probe..]);
                if (run != 0)
                    best.Consider(writer.RunSavings(probe, run), probe, run, -1);
                int sourceBound = writer.CopySavingsUpperBound(target.Length, pendingStart);
                source.Search(target, probe, pendingStart, in writer, ref best, sourceBound);
                history.Search(probe, source.SourceSegmentLength, in writer, ref best,
                    writer.CopySavingsUpperBound(target.Length, probe));
                if (best.Length == 0)
                {
                    probe++;
                    continue;
                }
                // A short target COPY can skip the next aligned source landmark.
                if (source.SourceSegmentLength != 0 && best.Length <= 64 && best.WriteOffset + best.Length < target.Length)
                {
                    int last = Math.Min(probe + 7, target.Length - 4);
                    for (int look = probe + 1; look <= last && look < best.WriteOffset + best.Length; look++)
                        source.Search(target, look, pendingStart, in writer, ref best, sourceBound);
                }
                writer.Add(target[pendingStart..best.WriteOffset]);
                if (best.IsRun) writer.Run(target[probe], best.Length);
                else writer.Copy(best.Address, best.Length);
                probe = best.WriteOffset + best.Length;
                pendingStart = probe;
            }
            writer.Add(target[pendingStart..]);
        }
        finally
        {
            history.Dispose();
        }
    }

    private static int RunLength(ReadOnlySpan<byte> tail)
    {
        byte value = tail[0];
        if (tail[1] != value || tail[2] != value) return 0;
        // An interior three-byte run can cost more than it saves by splitting a pending ADD.
        if (tail.Length == 3) return 3;
        if (tail[3] != value) return 0;
        int count = tail[4..].IndexOfAnyExcept(value);
        return count < 0 ? tail.Length : count + 4;
    }
}
