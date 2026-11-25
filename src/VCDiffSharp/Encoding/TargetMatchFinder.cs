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
        int required = WindowWriter.MinimumImprovingLength(best.Savings);
        if (required > tail.Length) return;
        if (_window.Length <= DirectLimit)
        {
            for (int position = probe - 1; position >= 0 && best.Savings < upperBound; position--)
                TryImprove(position, probe, dictionaryLength, in writer, ref best, required);
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
        // The oldest entry has first refusal on a tie. All other candidates are visited newest first.
        int oldest = first - 1;
        TryImprove(oldest, probe, dictionaryLength, in writer, ref best, required);
        int chainHead = first;
        int entry = Next(first);
        if (entry == first || best.Savings >= upperBound) return;
        if (best.Savings != 0)
            required = writer.MinimumTargetCopyLength(probe, best.Savings, tail.Length);
        if (required > tail.Length) return;

        // Every improving match also contains the four bytes at 'shift'. Prefer that bucket
        // when its most recent occurrence is further back, a cheap proxy for a rarer seed.
        int shift = Math.Min(required - 4, 16);
        if (shift > 0)
        {
            int anchor = _index.Entry(Bucket(tail[shift..], mask));
            int newestAnchor = anchor == 0 ? 0 : Next(anchor);
            if (newestAnchor < entry)
            {
                // Overlapping matches can put the alternate seed beyond the indexed history.
                for (int position = probe - 1; position >= probe - shift && position > oldest; position--)
                {
                    if (TryImprove(position, probe, dictionaryLength, in writer, ref best, required))
                        required = writer.MinimumTargetCopyLength(probe, best.Savings, tail.Length);
                    if (required > tail.Length || best.Savings >= upperBound) return;
                }
                chainHead = anchor;
                entry = newestAnchor;
            }
            else
                shift = 0;
        }

        while (entry - 1 - shift > oldest)
        {
            if (TryImprove(entry - 1 - shift, probe, dictionaryLength, in writer, ref best, required))
                required = writer.MinimumTargetCopyLength(probe, best.Savings, tail.Length);
            if (entry == chainHead || required > tail.Length || best.Savings >= upperBound) break;
            entry = Next(entry);
        }
    }

    private readonly int Next(int entry) => _index.Entry(_index.BucketCount + entry - 1);

    private readonly bool TryImprove(int position, int probe, int dictionaryLength, scoped in WindowWriter writer,
        scoped ref WindowChoice best, int required)
    {
        var earlier = _window[position..];
        var tail = _window[probe..];
        if (BinaryPrimitives.ReadUInt32LittleEndian(earlier) != BinaryPrimitives.ReadUInt32LittleEndian(tail)) return false;
        if (required > 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(earlier[(required - 4)..]) !=
            BinaryPrimitives.ReadUInt32LittleEndian(tail[(required - 4)..])) return false;
        int known = required <= 8 ? required : 4;
        int length = known + earlier[known..].CommonPrefixLength(tail[known..]);
        if (length < required) return false;
        int address = dictionaryLength + position;
        int savings = writer.CopySavings(probe, address, length);
        if (savings <= best.Savings) return false;
        best.Consider(savings, probe, length, address);
        return true;
    }

    private static int Bucket(ReadOnlySpan<byte> seed, int mask)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(seed);
        return (int)(unchecked(value * 2654435761u) >> (32 - BitOperations.Log2((uint)mask + 1))) & mask;
    }

    internal void Dispose() => _index.Dispose();
}
