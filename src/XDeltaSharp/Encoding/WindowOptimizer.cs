using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

/// <summary>Refines completed operations while the source and target of the window are available.</summary>
internal static class WindowOptimizer
{
    internal static byte[]? Finish(ref WindowWriter original, ReadOnlySpan<byte> source, int sourceOffset,
        ReadOnlySpan<byte> target, bool firstWindow, IBufferWriter<byte>? destination)
    {
        // A single literal or RUN with no addresses is already compared with literal framing.
        if (original.Addresses.IsEmpty && original.Instructions.Length <= 1)
            return original.Finish(target, firstWindow, destination);

        // Each default opcode contains at most one COPY or RUN. ADDs are implicit gaps.
        int capacity = Math.Min(original.Instructions.Length, target.Length);
        CopyMatch[]? rented = null;
        Span<CopyMatch> operations = capacity <= 32 ? stackalloc CopyMatch[capacity] :
            (rented = ArrayPool<CopyMatch>.Shared.Rent(capacity)).AsSpan(0, capacity);
        try
        {
            int count = ReadOperations(original.Instructions, original.Addresses, source.Length, operations);
            if (count == 0) return original.Finish(target, firstWindow, destination);
            operations = ExtendBackward(operations[..count], source, target);
            ExtendForward(operations, source, target);

            int first = source.Length, end = 0;
            foreach (var op in operations)
            {
                if (op.ReadOffset < 0 || op.ReadOffset >= source.Length) continue;
                first = Math.Min(first, op.ReadOffset);
                end = Math.Max(end, op.ReadOffset + op.Length);
            }
            int dictionarySize = end == 0 ? 0 : end - first;
            var candidate = new WindowWriter(end == 0 ? 0 : sourceOffset + first, dictionarySize,
                stackalloc byte[128], stackalloc byte[64], stackalloc byte[64], stackalloc int[768]);
            try
            {
                int written = 0;
                foreach (var op in operations)
                {
                    if (op.ReadOffset < 0)
                    {
                        if (op.Length <= 8 && candidate.RunSavings(op.WriteOffset, op.Length) <= 0) continue;
                        candidate.Add(target[written..op.WriteOffset]);
                        candidate.Run(target[op.WriteOffset], op.Length);
                    }
                    else
                    {
                        int address = op.ReadOffset < source.Length ? op.ReadOffset - first :
                            op.ReadOffset - source.Length + dictionarySize;
                        if (op.Length <= 8 && candidate.CopySavings(op.WriteOffset, address, op.Length) <= 0) continue;
                        candidate.Add(target[written..op.WriteOffset]);
                        candidate.Copy(address, op.Length);
                    }
                    written = op.End;
                }
                candidate.Add(target[written..]);
                if (candidate.FramedSize(target.Length) < original.FramedSize(target.Length))
                    return candidate.Finish(target, firstWindow, destination);
            }
            finally { candidate.Dispose(); }
            return original.Finish(target, firstWindow, destination);
        }
        finally
        {
            if (rented != null) ArrayPool<CopyMatch>.Shared.Return(rented);
        }
    }

    private static int ReadOperations(ReadOnlySpan<byte> codes, ReadOnlySpan<byte> operands, int sourceLength,
        Span<CopyMatch> operations)
    {
        var instructions = new SectionReader(codes);
        var addresses = new SectionReader(operands);
        Span<long> near = stackalloc long[4];
        Span<long> same = operands.IsEmpty ? [] : stackalloc long[3 * 256];
        near.Clear();
        same.Clear();
        int nextNear = 0, written = 0, count = 0;
        Instruction pending = default;
        while (!instructions.IsEmpty || pending.Kind != InstructionKind.NoOp)
        {
            Instruction current;
            if (pending.Kind == InstructionKind.NoOp)
                (current, pending) = DefaultCodeTable.Lookup(instructions.ReadByte());
            else
            {
                current = pending;
                pending = default;
            }
            int length = current.Size == 0 ? instructions.ReadLength() : current.Size;
            if (current.Kind != InstructionKind.Add)
            {
                int address = current.Kind == InstructionKind.Run ? -1 :
                    (int)AddressCache.ReadAddress(current.Mode, sourceLength + written, ref addresses, near, same, ref nextNear);
                operations[count++] = new CopyMatch(address, written, length);
            }
            written += length;
        }
        return count;
    }

    private static Span<CopyMatch> ExtendBackward(Span<CopyMatch> ops, ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
    {
        int limit = target.Length, output = ops.Length;
        for (int i = ops.Length - 1; i >= 0; i--)
        {
            var op = ops[i];
            int length = Math.Min(op.Length, limit - op.WriteOffset);
            if (length < (op.ReadOffset < 0 ? 3 : 4)) continue;
            int start = op.WriteOffset, address = op.ReadOffset;
            if (address < 0)
            {
                byte value = target[start];
                while (start > 0 && target[start - 1] == value) start--;
            }
            else
            {
                bool fromSource = address < source.Length;
                var dictionary = fromSource ? source : target;
                int position = fromSource ? address : address - source.Length;
                while (start > 0 && position > 0 && dictionary[position - 1] == target[start - 1])
                {
                    start--;
                    position--;
                }
                address -= op.WriteOffset - start;
            }
            ops[--output] = new CopyMatch(address, start, length + op.WriteOffset - start);
            limit = start;
        }
        return ops[output..];
    }

    private static void ExtendForward(Span<CopyMatch> ops, ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
    {
        for (int i = 0; i < ops.Length; i++)
        {
            var op = ops[i];
            int limit = i + 1 < ops.Length ? ops[i + 1].WriteOffset : target.Length;
            var gap = target[op.End..limit];
            if (gap.IsEmpty) continue;
            int extra;
            if (op.ReadOffset < 0)
            {
                extra = gap.IndexOfAnyExcept(target[op.WriteOffset]);
                if (extra < 0) extra = gap.Length;
            }
            else
            {
                var reference = op.ReadOffset < source.Length ? source[op.ReadOffset..] : target[(op.ReadOffset - source.Length)..];
                extra = gap.CommonPrefixLength(reference[op.Length..]);
            }
            ops[i] = op with { Length = op.Length + extra };
        }
    }
}
