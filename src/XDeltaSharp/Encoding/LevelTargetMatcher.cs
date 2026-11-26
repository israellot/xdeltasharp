using System.Buffers.Binary;
using System.Numerics;

namespace XDeltaSharp.Encoding;

/// <summary>Indexes visited target probes, newest first. Skipped match interiors use no links.</summary>
internal ref struct LevelTargetMatcher(ReadOnlySpan<byte> window, MatcherProfile profile, MemoryMode memory, Span<byte> scratch)
{
    private readonly ReadOnlySpan<byte> _window = window;
    private readonly Span<byte> _scratch = scratch;
    private MatchIndex _index;
    private int _ringMask;
    private const int MaxLinks = 1 << 18;

    private static int Links(int starts, MatcherProfile profile) => profile.TargetChain <= 1 ? 0 :
        Math.Min(MaxLinks, (int)BitOperations.RoundUpToPowerOf2((uint)starts));
    private static int Buckets(int starts, MemoryMode memory) => MatchIndex.Buckets((starts + 3) / 4, memory);

    internal static int ScratchSize(int length, MatcherProfile profile, MemoryMode memory)
    {
        int starts = length - 3;
        if (profile.TargetChain == 0 || starts is <= 0 or > byte.MaxValue) return 0;
        return Buckets(starts, memory) + Links(starts, profile);
    }

    internal CopyMatch FindAndInsert(int probe, int minimum, bool lazy)
    {
        if (_index.BucketCount == 0)
        {
            int starts = _window.Length - 3;
            int links = Links(starts, profile);
            _ringMask = links - 1;
            _index = new MatchIndex(starts, Buckets(starts, memory), _scratch, links);
        }
        var tail = _window[probe..];
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(tail);
        int bucket = (int)(unchecked(seed * 2654435761u) >> (32 - BitOperations.Log2((uint)_index.BucketCount)))
            & (_index.BucketCount - 1);
        int head = _index.Entry(bucket);
        int entry = head;
        CopyMatch best = default;
        int budget = lazy ? profile.LazyChain : profile.TargetChain;
        while (entry != 0 && budget-- > 0)
        {
            int position = entry - 1;
            var earlier = _window[position..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(earlier) == seed)
            {
                int length = 4 + tail[4..].CommonPrefixLength(earlier[4..]);
                if (length >= minimum && length > best.Length)
                {
                    best = new CopyMatch(position, probe, length);
                    if (length >= profile.LongEnough || length == tail.Length) break;
                }
            }
            if (budget == 0 || probe - position > _ringMask) break;
            entry = _index.Entry(_index.BucketCount + (position & _ringMask));
            if (entry == 0 || probe - (entry - 1) > _ringMask) break;
        }
        if (_ringMask >= 0) _index.Set(_index.BucketCount + (probe & _ringMask), head);
        _index.Set(bucket, probe + 1);
        return best;
    }

    public void Dispose() => _index.Dispose();
}
