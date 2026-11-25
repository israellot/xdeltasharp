// Source landmarks and hash adapted from DeltaSharp. Copyright (c) 2024 Israel Lot, MIT.
using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace VCDiffSharp.Encoding;

/// <summary>Indexes eight-byte source landmarks with compact, bounded collision chains.</summary>
internal readonly ref struct SourceMatchFinder
{
    private const int MaxCollisionHops = 256;

    private readonly ReadOnlySpan<byte> _source;
    private readonly Span<byte> _small;
    private readonly ushort[]? _medium;
    private readonly int[]? _large;
    private readonly bool _direct;

    internal int SourceSegmentLength => _source.Length;
    internal int BucketCount { get; }

    private static int GetBucketCount(int blocks, MemoryMode memoryMode)
    {
        int rounded = (int)BitOperations.RoundUpToPowerOf2((uint)blocks);
        return memoryMode == MemoryMode.Default ? 2 * rounded : rounded;
    }

    internal static int SmallTableSize(int sourceLength, int targetLength, MemoryMode memoryMode)
    {
        int blocks = sourceLength / 8;
        if (blocks is 0 or > byte.MaxValue || targetLength <= 32) return 0;
        return GetBucketCount(blocks, memoryMode) + blocks;
    }

    internal SourceMatchFinder(ReadOnlySpan<byte> source, int targetLength, MemoryMode memoryMode, Span<byte> smallTable)
    {
        // Building a large index for a handful of probes costs more than emitting the literals.
        bool disproportionateIndex = targetLength <= 256 && source.Length > Math.Max(2047, 64 * targetLength);
        if (source.Length < 4 || targetLength < 4 || disproportionateIndex)
            return;

        _source = source;
        if (targetLength <= 32 || source.Length < 8)
        {
            _direct = true;
            return;
        }

        int blocks = source.Length / 8;
        int buckets = GetBucketCount(blocks, memoryMode);
        BucketCount = buckets;
        if (blocks > ushort.MaxValue)
        {
            _large = ArrayPool<int>.Shared.Rent(buckets + blocks);
            _large.AsSpan(0, buckets).Clear();
        }
        else if (blocks > byte.MaxValue)
        {
            _medium = ArrayPool<ushort>.Shared.Rent(buckets + blocks);
            _medium.AsSpan(0, buckets).Clear();
        }
        else
        {
            _small = smallTable[..(buckets + blocks)];
            _small[..buckets].Clear();
        }

        // Heads precede links in pooled storage. Every link is overwritten during construction.
        // Prepending in reverse source order gives the earliest landmark precedence on ties.
        for (int i = blocks - 1; i >= 0; i--)
        {
            int bucket = Bucket(source.Slice(i * 8), buckets - 1);
            if (_large != null)
            {
                _large[buckets + i] = _large[bucket];
                _large[bucket] = i + 1;
            }
            else if (_medium != null)
            {
                _medium[buckets + i] = _medium[bucket];
                _medium[bucket] = (ushort)(i + 1);
            }
            else
            {
                _small[buckets + i] = _small[bucket];
                _small[bucket] = (byte)(i + 1);
            }
        }
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
        int block = Entry(bucket);
        CopyMatch best = default;
        for (int hops = 0; block != 0; hops++)
        {
            int index = block - 1;
            int position = index * 8;
            if (_source[position] == tail[0])
            {
                var match = Extend(window, probe, pendingStart, position);
                if (match.Length > best.Length)
                {
                    best = match;
                    if (best.Length == window.Length - pendingStart) break;
                }
            }
            if (hops == MaxCollisionHops) break;
            block = Entry(BucketCount + index);
        }
        return best;
    }

    private int Entry(int slot) => _large != null ? _large[slot] : _medium != null ? _medium[slot] : _small[slot];

    private CopyMatch Extend(ReadOnlySpan<byte> window, int probe, int pendingStart, int position)
    {
        int forward = window[probe..].CommonPrefixLength(_source[position..]);
        int backward = 0;
        int limit = Math.Min(position, probe - pendingStart);
        while (backward < limit && _source[position - backward - 1] == window[probe - backward - 1])
            backward++;
        return new CopyMatch(position - backward, probe - backward, forward + backward);
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

    public void Dispose()
    {
        if (_medium != null) ArrayPool<ushort>.Shared.Return(_medium);
        if (_large != null) ArrayPool<int>.Shared.Return(_large);
    }
}
