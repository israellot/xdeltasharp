using System.Buffers.Binary;
using System.Numerics;

namespace VCDiffSharp.Encoding;

/// <summary>Indexes four-byte seeds starting strictly before the probe, including skipped positions.</summary>
internal ref struct TargetMatchFinder(ReadOnlySpan<byte> window, Span<byte> scratch, MemoryMode memoryMode)
{
    private readonly ReadOnlySpan<byte> _window = window;
    private readonly Span<byte> _scratch = scratch;
    private MatchIndex _index;
    private int _indexed;

    // Tiny windows are cheaper to scan directly. Larger windows index every eligible start.
    private const int DirectLimit = 64;
    private static int Buckets(int starts, MemoryMode mode) => MatchIndex.Buckets((starts + 3) / 4, mode);

    internal static int SmallTableSize(int length, MemoryMode mode)
    {
        int starts = length - 3;
        if (length <= DirectLimit || starts > byte.MaxValue) return 0;
        return starts + Buckets(starts, mode);
    }

    internal void Search(int probe, int dictionaryLength, scoped in WindowWriter writer,
        scoped ref WindowChoice best, int upperBound)
    {
        if (probe == 0 || _window.Length - probe < 4 || best.Savings >= upperBound) return;
        var tail = _window[probe..];
        if (_window.Length <= DirectLimit)
        {
            for (int position = probe - 1; position >= 0 && best.Savings < upperBound; position--)
                Consider(position, probe, dictionaryLength, in writer, ref best);
            return;
        }

        if (_index.BucketCount == 0)
        {
            int starts = _window.Length - 3;
            _index = new MatchIndex(starts, Buckets(starts, memoryMode), _scratch);
        }
        int mask = _index.BucketCount - 1;
        while (_indexed < probe)
        {
            _index.AddTargetPosition(Bucket(_window[_indexed..], mask), _indexed);
            _indexed++;
        }
        int first = _index.Entry(Bucket(tail, mask));
        if (first == 0) return;
        int entry = first;
        do
        {
            int position = entry - 1;
            Consider(position, probe, dictionaryLength, in writer, ref best);
            if (best.Savings >= upperBound) break;
            entry = _index.Entry(_index.BucketCount + position);
        } while (entry != first);
    }

    private readonly void Consider(int position, int probe, int dictionaryLength, scoped in WindowWriter writer,
        scoped ref WindowChoice best)
    {
        var earlier = _window[position..];
        var tail = _window[probe..];
        if (BinaryPrimitives.ReadUInt32LittleEndian(earlier) != BinaryPrimitives.ReadUInt32LittleEndian(tail)) return;
        // A COPY can save at most its length plus four bytes of ADD overhead. Check a required
        // word first when a previous candidate already has substantial savings.
        int required = best.Savings - 4;
        if (required >= 8 && (required > tail.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(earlier[(required - 4)..]) !=
            BinaryPrimitives.ReadUInt32LittleEndian(tail[(required - 4)..]))) return;
        // Comparing against the original input permits matches longer than the backward distance.
        int length = 4 + earlier[4..].CommonPrefixLength(tail[4..]);
        int address = dictionaryLength + position;
        best.Consider(writer.CopySavings(probe, address, length), probe, length, address);
    }

    private static int Bucket(ReadOnlySpan<byte> seed, int mask)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(seed);
        return (int)(unchecked(value * 2654435761u) >> (32 - BitOperations.Log2((uint)mask + 1))) & mask;
    }

    internal void Dispose() => _index.Dispose();
}
