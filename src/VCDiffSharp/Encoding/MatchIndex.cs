using System.Buffers;
using System.Numerics;

namespace VCDiffSharp.Encoding;

/// <summary>One-based bucket heads and collision links, packed to the width of the largest position.</summary>
internal readonly ref struct MatchIndex
{
    private readonly Span<byte> _small;
    private readonly ushort[]? _medium;
    private readonly int[]? _large;
    internal int BucketCount { get; }

    internal static int Buckets(int desired, MemoryMode memoryMode)
    {
        int rounded = (int)BitOperations.RoundUpToPowerOf2((uint)desired);
        return memoryMode == MemoryMode.Default ? 2 * rounded : rounded;
    }

    internal MatchIndex(int positions, int buckets, Span<byte> scratch, int? links = null)
    {
        BucketCount = buckets;
        int entries = buckets + (links ?? positions);
        if (positions <= byte.MaxValue)
        {
            _small = scratch[..entries];
            _small[..buckets].Clear();
        }
        else if (positions <= ushort.MaxValue)
        {
            _medium = ArrayPool<ushort>.Shared.Rent(entries);
            _medium.AsSpan(0, buckets).Clear();
        }
        else
        {
            _large = ArrayPool<int>.Shared.Rent(entries);
            _large.AsSpan(0, buckets).Clear();
        }
    }

    internal void Prepend(int bucket, int index)
    {
        int link = BucketCount + index;
        if (_large != null)
        {
            _large[link] = _large[bucket];
            _large[bucket] = index + 1;
        }
        else if (_medium != null)
        {
            _medium[link] = _medium[bucket];
            _medium[bucket] = (ushort)(index + 1);
        }
        else
        {
            _small[link] = _small[bucket];
            _small[bucket] = (byte)(index + 1);
        }
    }

    internal int Entry(int slot) => _large != null ? _large[slot] : _medium != null ? _medium[slot] : _small[slot];

    // Keep the oldest occurrence first, followed by the newest and progressively older
    // entries. On interrupted periodic data the oldest occurrence can span the whole tail.
    internal void AddTargetPosition(int bucket, int index)
    {
        int first = Entry(bucket);
        int current = index + 1;
        if (first == 0)
        {
            Set(bucket, current);
            Set(BucketCount + index, current);
        }
        else
        {
            int firstLink = BucketCount + first - 1;
            Set(BucketCount + index, Entry(firstLink));
            Set(firstLink, current);
        }
    }

    internal void Set(int slot, int value)
    {
        if (_large != null) _large[slot] = value;
        else if (_medium != null) _medium[slot] = (ushort)value;
        else _small[slot] = (byte)value;
    }

    internal void Dispose()
    {
        if (_medium != null) ArrayPool<ushort>.Shared.Return(_medium);
        if (_large != null) ArrayPool<int>.Shared.Return(_large);
    }
}
