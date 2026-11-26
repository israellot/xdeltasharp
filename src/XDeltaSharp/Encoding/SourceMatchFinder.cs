// Source landmarks and hash adapted from DeltaSharp. Copyright (c) 2024 Israel Lot, MIT.
using System.Buffers.Binary;

namespace XDeltaSharp.Encoding;

/// <summary>Indexes eight-byte source landmarks for bounded or full collision-chain search.</summary>
internal readonly ref struct SourceMatchFinder
{
    private const int MaxCollisionHops = 256;

    private readonly ReadOnlySpan<byte> _source;
    private readonly MatchIndex _index;
    private readonly int _indexMask;
    private readonly bool _direct;

    internal int SourceSegmentLength => _source.Length;
    internal int BucketCount => _index.BucketCount;

    internal static int SmallTableSize(int sourceLength, int targetLength, MemoryMode memoryMode)
    {
        int blocks = sourceLength / 8;
        if (blocks is 0 or > byte.MaxValue || targetLength <= 32) return 0;
        return MatchIndex.Buckets(blocks, memoryMode) + blocks;
    }

    internal SourceMatchFinder(ReadOnlySpan<byte> source, int targetLength, MemoryMode memoryMode, Span<byte> smallTable,
        VcdiffEncodeMode mode = VcdiffEncodeMode.Fast)
    {
        // Building a large index for a handful of probes costs more than emitting the literals.
        bool disproportionateIndex = targetLength <= 256 && source.Length > Math.Max(2047, 64 * targetLength);
        if (source.Length < 4 || targetLength < 4 || (disproportionateIndex && mode == VcdiffEncodeMode.Fast))
            return;

        _source = source;
        if (targetLength <= 32 || source.Length < 8 || disproportionateIndex)
        {
            _direct = true;
            return;
        }

        int blocks = source.Length / 8;
        _index = new MatchIndex(blocks, MatchIndex.Buckets(blocks, memoryMode), smallTable);
        // The 64 MiB source segment cap leaves room for all one-based landmark indices in 24 bits.
        _indexMask = mode == VcdiffEncodeMode.Fast && blocks is > ushort.MaxValue and <= MatchIndex.PackedIndexMask
            ? MatchIndex.PackedIndexMask : -1;
        if (_indexMask == MatchIndex.PackedIndexMask)
        {
            for (int i = blocks - 1; i >= 0; i--)
            {
                var seed = source[(i * 8)..];
                _index.PrependWithMask(Bucket(seed, BucketCount - 1), i, seed[0]);
            }
            return;
        }
        for (int i = blocks - 1; i >= 0; i--)
            _index.Prepend(Bucket(source[(i * 8)..], BucketCount - 1), i);
    }

    internal CopyMatch FindMatch(ReadOnlySpan<byte> window, int probe, int pendingStart)
    {
        var tail = window[probe..];
        if (_source.IsEmpty)
            return default;

        if (_direct)
        {
            if (tail.Length < 4) return default;
            int position = _source.IndexOf(tail[..4]);
            return position < 0 ? default : Extend(window, probe, pendingStart, position);
        }

        if (tail.Length < 8) return default;
        int bucket = Bucket(tail, BucketCount - 1);
        int block = _index.Entry(bucket);
        // -1 gives the ordinary nonzero-link test for an unpacked table.
        int wanted = _indexMask == MatchIndex.PackedIndexMask ? MatchIndex.FirstByteBit(tail[0]) : -1;
        CopyMatch best = default;
        for (int hops = 0; (block & wanted) != 0; hops++)
        {
            int index = (block & _indexMask) - 1;
            int position = index * 8;
            if (_source[position] == tail[0] && CanBeat(window, probe, pendingStart, position, best.Length))
            {
                var match = Extend(window, probe, pendingStart, position);
                if (match.Length > best.Length)
                {
                    best = match;
                    if (best.Length == window.Length - pendingStart) break;
                }
            }
            if (hops == MaxCollisionHops) break;
            block = _index.Entry(BucketCount + index);
        }
        return best;
    }

    internal void Search(ReadOnlySpan<byte> window, int probe, int pendingStart, scoped in WindowWriter writer,
        scoped ref WindowChoice best, int upperBound)
    {
        if (_source.IsEmpty || best.Savings >= upperBound) return;
        var tail = window[probe..];
        if (_direct)
        {
            if (tail.Length < 4) return;
            int start = 0;
            while (start <= _source.Length - 4)
            {
                int hit = _source[start..].IndexOf(tail[..4]);
                if (hit < 0) break;
                int position = start + hit;
                Consider(window, probe, pendingStart, position, in writer, ref best);
                if (best.Savings >= upperBound) break;
                start = position + 1;
            }
            return;
        }

        if (tail.Length < 8) return;
        int block = _index.Entry(Bucket(tail, BucketCount - 1)) & _indexMask;
        while (block != 0)
        {
            int index = block - 1;
            int position = index * 8;
            if (_source[position] == tail[0])
            {
                Consider(window, probe, pendingStart, position, in writer, ref best);
                if (best.Savings >= upperBound) break;
            }
            block = _index.Entry(BucketCount + index) & _indexMask;
        }
    }

    private void Consider(ReadOnlySpan<byte> window, int probe, int pendingStart, int position,
        scoped in WindowWriter writer, scoped ref WindowChoice best)
    {
        var match = Extend(window, probe, pendingStart, position);
        if (match.Length < WindowWriter.MinimumImprovingLength(best.Savings)) return;
        int savings = writer.CopySavings(match.WriteOffset, match.ReadOffset, match.Length);
        best.Consider(savings, match.WriteOffset, match.Length, match.ReadOffset);
    }

    private CopyMatch Extend(ReadOnlySpan<byte> window, int probe, int pendingStart, int position)
    {
        int forward = window[probe..].CommonPrefixLength(_source[position..]);
        int backward = 0;
        int limit = Math.Min(position, probe - pendingStart);
        while (backward < limit && _source[position - backward - 1] == window[probe - backward - 1])
            backward++;
        return new CopyMatch(position - backward, probe - backward, forward + backward);
    }

    private bool CanBeat(ReadOnlySpan<byte> window, int probe, int pendingStart, int position, int bestLength)
    {
        if (bestLength < 8) return true;
        // Even with the longest possible backward extension, this forward byte
        // must match to improve on the incumbent. Equal lengths keep the earlier landmark.
        int requiredOffset = bestLength - Math.Min(position, probe - pendingStart);
        return requiredOffset < 0 || (requiredOffset < window.Length - probe &&
            requiredOffset < _source.Length - position && window[probe + requiredOffset] == _source[position + requiredOffset]);
    }

    internal bool ShouldUseCopyPrediction(ReadOnlySpan<byte> window, int alignedOffset)
    {
        if (_direct || window.Length < 64 || _source.Length < 48) return false;
        // Inspect at most two short neighborhoods. Checking only the bucket head keeps the sampling work bounded.
        int center = window.Length / 2;
        return HasAlignedSample(window, 0, alignedOffset) || (window.Length >= 1024 && HasAlignedSample(window, center, alignedOffset));
    }

    private bool HasAlignedSample(ReadOnlySpan<byte> window, int start, int alignedOffset)
    {
        for (int p = start; p < start + 8; p++)
        {
            var sample = window.Slice(p, 48);
            int aligned = alignedOffset + p;
            if ((uint)aligned <= (uint)(_source.Length - 12) &&
                sample[..12].SequenceEqual(_source.Slice(aligned, 12)))
                return true;
            int block = _index.Entry(Bucket(sample, BucketCount - 1)) & _indexMask;
            if (block == 0) continue;
            int position = (block - 1) * 8;
            int length = Math.Abs((long)position - aligned) <= 64 ? 12 : 48;
            if (position <= _source.Length - length && sample[..length].SequenceEqual(_source.Slice(position, length)))
                return true;
        }
        return false;
    }

    internal CopyMatch FindPredictedMatch(ReadOnlySpan<byte> window, int probe, int pendingStart, int sourceDelta)
    {
        int position = probe + sourceDelta;
        var tail = window[probe..];
        if (_source.Length < 8 || (uint)position > (uint)(_source.Length - 8) || tail.Length < 8)
            return default;
        if (BinaryPrimitives.ReadUInt64LittleEndian(tail) != BinaryPrimitives.ReadUInt64LittleEndian(_source[position..]))
            return default;
        return Extend(window, probe, pendingStart, position);
    }

    private static int Bucket(ReadOnlySpan<byte> seed, int mask)
    {
        ulong x = BinaryPrimitives.ReadUInt64LittleEndian(seed);
        unchecked
        {
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            return (int)(x ^ (x >> 31)) & mask;
        }
    }

    public void Dispose() => _index.Dispose();
}
