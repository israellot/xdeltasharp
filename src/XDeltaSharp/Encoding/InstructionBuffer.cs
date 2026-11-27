// Managed adaptation of xdelta3 instruction buffering. See THIRD-PARTY-NOTICES.txt.
using System.Buffers;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

/// <summary>Defers COPY and RUN emission so a later source match can cover earlier decisions.</summary>
internal ref struct InstructionBuffer(Span<CopyMatch> scratch)
{
    private const int CapacityLimit = 32768;
    private Span<CopyMatch> _items = scratch;
    private CopyMatch[]? _rented;
    private int _count;
    internal int Written { get; private set; }
    internal readonly int LastEnd => _count == 0 ? Written : _items[_count - 1].End;

    internal void Add(CopyMatch next, ReadOnlySpan<byte> target, ref WindowWriter writer)
    {
        // Ends are strictly increasing. A backward extension can subsume multiple whole matches.
        while (_count > 0 && _items[_count - 1].WriteOffset >= next.WriteOffset) _count--;
        if (_count == _items.Length)
        {
            var larger = ArrayPool<CopyMatch>.Shared.Rent(Math.Min(CapacityLimit, _count * 2));
            _items[.._count].CopyTo(larger);
            if (_rented != null) ArrayPool<CopyMatch>.Shared.Return(_rented);
            _rented = larger;
            _items = larger.AsSpan(0, Math.Min(larger.Length, CapacityLimit));
        }
        _items[_count++] = next;
        if (_count == CapacityLimit) Flush(target, ref writer, final: false);
    }

    internal void Flush(ReadOnlySpan<byte> target, ref WindowWriter writer, bool final)
    {
        // Keep the newest half available for future backward matches when the buffer fills.
        int stop = final ? _count : _count / 2;
        int i = 0;
        while (i < stop)
        {
            var current = ResolveOverlaps(_items[i++], ref i);
            if (!final && i == _count)
            {
                // This last match may still be under lazy evaluation at the current probe.
                _items[0] = current;
                _count = 1;
                return;
            }
            writer.Add(target[Written..current.WriteOffset]);
            if (current.IsRun)
                writer.Run(target[current.WriteOffset], current.Length);
            else
                writer.Copy(current.ReadOffset, current.Length);
            Written = current.End;
        }
        _items[i.._count].CopyTo(_items);
        _count -= i;
        if (final) writer.Add(target[Written..]);
    }

    /// <summary>Settles <paramref name="current"/> against the pending matches it overlaps, starting at
    /// <paramref name="next"/>, and returns the operation to emit. On return <paramref name="next"/> is the
    /// first pending match that begins at or after its end.</summary>
    private CopyMatch ResolveOverlaps(CopyMatch current, ref int next)
    {
        while (next < _count && current.End > _items[next].WriteOffset)
        {
            var following = _items[next];
            // Skip the middle match if the one after it reaches back to this boundary.
            if (next + 1 < _count && _items[next + 1].WriteOffset <= current.End + 1)
            {
                next++;
                continue;
            }

            int currentOnlyBytes = following.WriteOffset - current.WriteOffset;
            int followingOnlyBytes = following.End - current.End;
            if (currentOnlyBytes < DefaultCodeTable.MinFixedCopySize && followingOnlyBytes < DefaultCodeTable.MinFixedCopySize)
            {
                if (following.Length > current.Length) current = following;
                next++;
                continue;
            }

            (current, _items[next]) = SplitOverlap(current, following);
            break;
        }
        return current;
    }

    // Preserve both matches when possible. Keep one size in the default table's
    // immediate range (4..18) when dividing their overlap.
    private static (CopyMatch Earlier, CopyMatch Later) SplitOverlap(CopyMatch earlier, CopyMatch later)
    {
        int halfLength = (later.End - earlier.WriteOffset) / 2;
        int split = Math.Clamp(earlier.WriteOffset + Math.Min(DefaultCodeTable.MaxFixedCopySize, halfLength),
            later.WriteOffset, earlier.End);
        split = Math.Clamp(split, earlier.WriteOffset + DefaultCodeTable.MinFixedCopySize,
            later.End - DefaultCodeTable.MinFixedCopySize);
        int trim = split - later.WriteOffset;
        return (earlier with { Length = split - earlier.WriteOffset }, later with
        {
            WriteOffset = split,
            Length = later.Length - trim,
            ReadOffset = later.IsRun ? CopyMatch.RunOffset : later.ReadOffset + trim
        });
    }

    public void Dispose()
    {
        if (_rented != null) ArrayPool<CopyMatch>.Shared.Return(_rented);
    }
}
