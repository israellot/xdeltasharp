using System.Buffers;

namespace VCDiffSharp.Encoding;

internal static class WindowEncoder
{
    internal const int TargetWindowSize = 8 << 20;
    private const int SourceSegmentSize = 64 << 20;

    internal static byte[]? EncodeFast(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte>? destination)
    {
        // A single window knows its final size before writing. Only multi-window array results need aggregation.
        ArrayBufferWriter<byte>? collected = null;
        if (destination == null && target.Length > TargetWindowSize)
            destination = collected = new ArrayBufferWriter<byte>();

        int start = 0;
        int segmentLength = Math.Min(source.Length, SourceSegmentSize);
        do
        {
            int segmentStart = SegmentStart(source.Length, start);
            using var finder = new SourceMatchFinder(source.Slice(segmentStart, segmentLength), target.Length - start);
            do
            {
                int length = Math.Min(target.Length - start, TargetWindowSize);
                byte[]? result = EncodeWindow(in finder, segmentStart, target.Slice(start, length), start == 0, destination);
                if (destination == null) return result;
                start += length;
            } while (start < target.Length && SegmentStart(source.Length, start) == segmentStart);
        } while (start < target.Length);
        return collected?.WrittenSpan.ToArray();
    }

    private static int SegmentStart(int sourceLength, int targetOffset)
        => Math.Min(targetOffset / SourceSegmentSize * SourceSegmentSize, Math.Max(0, sourceLength - SourceSegmentSize));

    private static byte[]? EncodeWindow(in SourceMatchFinder finder, int segmentOffset, ReadOnlySpan<byte> target,
        bool firstWindow, IBufferWriter<byte>? destination)
    {
        Span<int> same = target.Length >= 128 && finder.SourceSegmentLength >= 128 ? stackalloc int[768] : [];
        var writer = new WindowWriter(segmentOffset, finder.SourceSegmentLength,
            stackalloc byte[128], stackalloc byte[64], stackalloc byte[64], same);
        try
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
            return writer.Finish(target, firstWindow, destination);
        }
        finally
        {
            writer.Dispose();
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
