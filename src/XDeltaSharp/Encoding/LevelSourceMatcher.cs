using System.Buffers.Binary;

namespace XDeltaSharp.Encoding;

/// <summary>One nine-byte landmark per bucket, sampled at the level's source stride.</summary>
internal readonly ref struct LevelSourceMatcher
{
    private const int DirectSearchTargetLimit = 32;
    private const int ShortTargetLimit = 256;
    private const int LargeDictionaryThreshold = 2047;
    private const int MaxSourceToShortTargetRatio = 64;
    private readonly ReadOnlySpan<byte> _source;
    private readonly MatchIndex _index;
    private readonly int _step;
    internal int Length => _source.Length;

    private static int Positions(int length, int step) => length < MatcherProfile.SourceSeed
        ? 0 : 1 + (length - MatcherProfile.SourceSeed) / step;

    internal static int ScratchSize(int length, int targetLength, int step, MemoryMode memory)
    {
        int positions = Positions(length, step);
        if (positions is 0 or > byte.MaxValue || targetLength <= DirectSearchTargetLimit) return 0;
        return MatchIndex.Buckets(positions, memory);
    }

    internal LevelSourceMatcher(ReadOnlySpan<byte> source, int targetLength, int step, MemoryMode memory, Span<byte> scratch)
    {
        _source = source;
        _step = step;
        int positions = Positions(source.Length, step);
        // A short target can be cheaper to search directly than to index a much larger dictionary.
        bool disproportionateIndex = targetLength <= ShortTargetLimit &&
            source.Length > Math.Max(LargeDictionaryThreshold, targetLength * MaxSourceToShortTargetRatio);
        if (targetLength <= DirectSearchTargetLimit || positions == 0 || disproportionateIndex) return;
        _index = new MatchIndex(positions, MatchIndex.Buckets(positions, memory), scratch, links: 0);
        // Populate backwards so the earliest occurrence wins a bucket collision.
        for (int block = positions - 1; block >= 0; block--)
            _index.Set(Bucket(source[(block * step)..]), block + 1);
    }

    private int Bucket(ReadOnlySpan<byte> seed)
    {
        ulong key = BinaryPrimitives.ReadUInt64LittleEndian(seed) ^ ((ulong)seed[8] * 0x9E3779B185EBCA87UL);
        return (int)(unchecked((key ^ (key >> 31)) * 0xBF58476D1CE4E5B9UL) >> 32) & (_index.BucketCount - 1);
    }

    internal CopyMatch Find(ReadOnlySpan<byte> window, int probe, int pending, int minimum, long predicted)
    {
        var tail = window[probe..];
        if (minimum > _source.Length || minimum > tail.Length) return default;
        if ((ulong)predicted <= (ulong)(_source.Length - minimum))
        {
            var match = Extend(window, probe, pending, minimum, (int)predicted);
            if (match.Length != 0) return match;
        }
        int position;
        if (_index.BucketCount == 0)
        {
            position = _source.IndexOf(tail[..4]);
            if (position < 0) return default;
        }
        else
        {
            if (tail.Length < MatcherProfile.SourceSeed) return default;
            int block = _index.Entry(Bucket(tail));
            if (block == 0) return default;
            position = (block - 1) * _step;
        }
        return position == predicted ? default : Extend(window, probe, pending, minimum, position);
    }

    private CopyMatch Extend(ReadOnlySpan<byte> window, int probe, int pending, int minimum, int position)
    {
        var reference = _source[position..];
        var tail = window[probe..];
        if (reference.Length < minimum || BinaryPrimitives.ReadUInt32LittleEndian(reference) !=
            BinaryPrimitives.ReadUInt32LittleEndian(tail)) return default;
        int forward = 4 + tail[4..].CommonPrefixLength(reference[4..]);
        if (forward < minimum) return default;
        int back = 0;
        int limit = Math.Min(position, probe - pending);
        while (back < limit && window[probe - back - 1] == _source[position - back - 1]) back++;
        return new CopyMatch(position - back, probe - back, forward + back);
    }

    public void Dispose() => _index.Dispose();
}
