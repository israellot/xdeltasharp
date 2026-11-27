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
        // Finish already compares a lone ADD or RUN with a literal-only window.
        if (original.IsEmptyOrSingleDataOperation)
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
                if (op.IsRun || op.ReadOffset >= source.Length) continue;
                first = Math.Min(first, op.ReadOffset);
                end = Math.Max(end, op.ReadOffset + op.Length);
            }
            int dictionarySize = end == 0 ? 0 : end - first;
            var candidate = new WindowWriter(end == 0 ? 0 : sourceOffset + first, dictionarySize,
                stackalloc byte[128], stackalloc byte[64], stackalloc byte[64], stackalloc int[AddressCache.SameSize]);
            try
            {
                int written = 0;
                foreach (var op in operations)
                {
                    if (op.IsRun)
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
                var candidateLayout = candidate.Measure(target.Length);
                var originalLayout = original.Measure(target.Length);
                return candidateLayout.Length < originalLayout.Length
                    ? candidate.Finish(target, firstWindow, destination, candidateLayout)
                    : original.Finish(target, firstWindow, destination, originalLayout);
            }
            finally
            {
                candidate.Dispose();
            }
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
        Span<long> near = stackalloc long[AddressCache.NearSize];
        Span<long> same = operands.IsEmpty ? [] : stackalloc long[AddressCache.SameSize];
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
                int address = current.Kind == InstructionKind.Run ? CopyMatch.RunOffset :
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
            if (length < (op.IsRun ? 3 : 4)) continue;
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
                int back = CopyMatch.CommonSuffixLength(dictionary[..position], target[..start]);
                start -= back;
                address -= back;
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
            if (op.IsRun)
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
