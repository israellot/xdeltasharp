// Managed adaptation of xdelta3 instruction buffering. See THIRD-PARTY-NOTICES.txt.
using System.Buffers;

namespace VCDiffSharp.Encoding;

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
            var current = _items[i++];
            while (i < _count && current.End > _items[i].WriteOffset)
            {
                var next = _items[i];
                if (i + 1 < _count && _items[i + 1].WriteOffset <= current.End + 1)
                {
                    i++;
                    continue;
                }

                int prefix = next.WriteOffset - current.WriteOffset;
                int suffix = next.End - current.End;
                if (prefix < 4 && suffix < 4)
                {
                    if (next.Length > current.Length) current = next;
                    i++;
                    continue;
                }

                // Preserve both matches when possible. Keep one size in the default table's
                // immediate range (4..18) when dividing their overlap.
                int split = Math.Clamp(current.WriteOffset + Math.Min(18, (next.End - current.WriteOffset) / 2),
                    next.WriteOffset, current.End);
                split = Math.Clamp(split, current.WriteOffset + 4, next.End - 4);
                int trim = split - next.WriteOffset;
                current = current with { Length = split - current.WriteOffset };
                _items[i] = next with { WriteOffset = split, Length = next.Length - trim,
                    ReadOffset = next.ReadOffset < 0 ? -1 : next.ReadOffset + trim };
                break;
            }
            if (!final && i == _count)
            {
                // This last match may still be under lazy evaluation at the current probe.
                _items[0] = current;
                _count = 1;
                return;
            }
            writer.Add(target[Written..current.WriteOffset]);
            if (current.ReadOffset < 0) writer.Run(target[current.WriteOffset], current.Length);
            else writer.Copy(current.ReadOffset, current.Length);
            Written = current.End;
        }
        _items[i.._count].CopyTo(_items);
        _count -= i;
        if (final) writer.Add(target[Written..]);
    }

    public void Dispose()
    {
        if (_rented != null) ArrayPool<CopyMatch>.Shared.Return(_rented);
    }
}
