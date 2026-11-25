using System.Buffers;
using System.Runtime.CompilerServices;

namespace VCDiffSharp.Encoding;

internal static class WindowEncoder
{
    internal const int TargetWindowSize = 8 << 20;
    private const int SourceSegmentSize = 64 << 20;

    internal static byte[]? Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte>? destination,
        VcdiffEncodeMode mode, MemoryMode memoryMode, int compressionLevel = MatcherProfile.DefaultLevel, bool optimize = false)
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
                    destination, mode, memoryMode, profile, segment, optimize);
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
        MemoryMode memoryMode, MatcherProfile profile, ReadOnlySpan<byte> segment, bool optimize)
    {
        Span<int> same = target.Length >= 128 && (mode != VcdiffEncodeMode.Fast || finder.SourceSegmentLength >= 128) ? stackalloc int[768] : [];
        var writer = new WindowWriter(segmentOffset, mode == VcdiffEncodeMode.Default ? levelSource.Length : finder.SourceSegmentLength,
            stackalloc byte[128], stackalloc byte[64], stackalloc byte[64], same);
        try
        {
            if (mode == VcdiffEncodeMode.Fast)
                ParseFast(in finder, target, targetOffset - segmentOffset, ref writer);
            else if (mode == VcdiffEncodeMode.Default)
                LevelParser.Parse(in levelSource, target, targetOffset - segmentOffset, profile, memoryMode, stackalloc CopyMatch[16], ref writer);
            else
                ParseExhaustive(in finder, target, memoryMode, ref writer);
            if (optimize)
            {
                int sourceLength = mode == VcdiffEncodeMode.Default ? levelSource.Length : finder.SourceSegmentLength;
                return WindowOptimizer.Finish(ref writer, segment[..sourceLength], segmentOffset,
                    target, targetOffset == 0, destination);
            }
            return writer.Finish(target, targetOffset == 0, destination);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void ParseFast(scoped in SourceMatchFinder finder, ReadOnlySpan<byte> target, int sourceDelta,
        ref WindowWriter writer)
    {
        if (finder.SourceSegmentLength == 0)
        {
            WriteLiteralRuns(target, ref writer);
            return;
        }
        // Pass a constant per window so the JIT can specialize the ordinary and predictive loops.
        if (finder.ShouldUseCopyPrediction(target, sourceDelta))
            ParseFastCore(in finder, target, sourceDelta, ref writer, true);
        else
            ParseFastCore(in finder, target, sourceDelta, ref writer, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseFastCore(scoped in SourceMatchFinder finder, ReadOnlySpan<byte> target, int sourceDelta,
        ref WindowWriter writer, bool lazy)
    {
        const int predictionLength = 12;
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
                // A short match elsewhere in the source can hide a resumption of the previous
                // alignment immediately after an edit. Verify one predicted seed before emitting.
                if (lazy && match.Length <= 18 && match.ReadOffset - match.WriteOffset != sourceDelta &&
                    match.End > probe + 1 && target.Length - match.End >= 8)
                {
                    var next = finder.FindPredictedMatch(target, probe + 1, pendingStart, sourceDelta);
                    if (next.Length >= match.Length + 2 && next.End > match.End)
                        match = next;
                }
                writer.Add(target[pendingStart..match.WriteOffset]);
                writer.Copy(match.ReadOffset, match.Length);
                probe = match.End;
                // Short incidental duplicates make poor predictors. Avoid immediately testing a
                // new alignment after a small chunk jumps to a distant part of the source.
                if (lazy && match.Length >= predictionLength)
                {
                    int delta = match.ReadOffset - match.WriteOffset;
                    bool tryContinuation = match.Length >= 64 || Math.Abs(delta - (long)sourceDelta) <= 64;
                    sourceDelta = delta;
                    while (tryContinuation && target.Length - probe > predictionLength)
                    {
                        // The byte after a maximal COPY differs. Resume after a single literal
                        // only when enough subsequent source bytes match at the same alignment.
                        var next = finder.FindPredictedMatch(target, probe + 1, probe + 1, sourceDelta);
                        if (next.Length < predictionLength) break;
                        if (target[probe + 1] == target[probe + 2] && target[probe + 1] == target[probe + 3] &&
                            target[probe + 1] == target[probe + 4]) break; // Let the normal RUN check decide.
                        writer.Add(target.Slice(probe, 1));
                        writer.Copy(next.ReadOffset, next.Length);
                        probe = next.End;
                    }
                }
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

    private static void WriteLiteralRuns(ReadOnlySpan<byte> target, ref WindowWriter writer)
    {
        int pendingStart = 0;
        int probe = 0;
        while (probe <= target.Length - 3)
        {
            int run = RunLength(target[probe..]);
            if (run == 0)
            {
                probe++;
                continue;
            }
            writer.Add(target[pendingStart..probe]);
            writer.Run(target[probe], run);
            probe += run;
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
