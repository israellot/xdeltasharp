// Source landmarks and hash adapted from DeltaSharp. Copyright (c) 2024 Israel Lot, MIT.
using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VCDiffSharp.Encoding;

/// <summary>Fast's source index stores the first eight-byte landmark in each bucket.</summary>
internal readonly ref struct SourceMatchFinder
{
    [InlineArray(256)]
    private struct ByteHeads { private byte _first; }

    private readonly ReadOnlySpan<byte> _source;
    private readonly ByteHeads _small;
    private readonly ushort[]? _medium;
    private readonly int[]? _large;
    private readonly int _mask;
    private readonly bool _direct;

    internal int SourceSegmentLength => _source.Length;

    internal SourceMatchFinder(ReadOnlySpan<byte> source, int targetLength)
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
        int buckets = (int)BitOperations.RoundUpToPowerOf2((uint)blocks);
        _mask = buckets - 1;
        if (blocks > ushort.MaxValue)
        {
            _large = ArrayPool<int>.Shared.Rent(buckets);
            _large.AsSpan(0, buckets).Clear();
        }
        else if (blocks > byte.MaxValue)
        {
            _medium = ArrayPool<ushort>.Shared.Rent(buckets);
            _medium.AsSpan(0, buckets).Clear();
        }
        else
            _mask = 255;

        // One-based block numbers reserve zero for an empty bucket.
        for (int i = blocks - 1; i >= 0; i--)
        {
            int bucket = Bucket(source.Slice(i * 8), _mask);
            if (_large != null) _large[bucket] = i + 1;
            else if (_medium != null) _medium[bucket] = (ushort)(i + 1);
            else _small[bucket] = (byte)(i + 1);
        }
    }

    internal CopyMatch FindMatch(ReadOnlySpan<byte> window, int probe, int pendingStart)
    {
        var tail = window[probe..];
        if (_source.IsEmpty)
            return default;

        int position;
        if (_direct)
        {
            if (tail.Length < 4) return default;
            position = _source.IndexOf(tail[..4]);
            if (position < 0) return default;
        }
        else
        {
            if (tail.Length < 8) return default;
            int bucket = Bucket(tail, _mask);
            int block = _large != null ? _large[bucket] : _medium != null ? _medium[bucket] : _small[bucket];
            if (block == 0) return default;
            position = (block - 1) * 8;
        }

        int forward = tail.CommonPrefixLength(_source[position..]);
        if (forward == 0) return default;
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
