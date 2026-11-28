using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

/// <summary>Refines completed operations while the source and target of the window are available.</summary>
internal static class WindowOptimizer
{
    private const int OperationScratch = 32;

    // A RUN costs three bytes (opcode, size, value) and the shortest COPY opcode covers four,
    // so an operation trimmed below these lengths cannot pay for itself.
    private const int MinRunLength = 3;
    private const int MinCopyLength = 4;

    // Operations this short can cost more than the literals they replace, so they are priced before use.
    private const int MaxLengthToPrice = 8;

    /// <summary>Writes the window from <paramref name="original"/> or from a refined copy of its operations,
    /// whichever is smaller.</summary>
    internal static byte[]? FinishSmallest(ref WindowWriter original, ReadOnlySpan<byte> source, int sourceOffset,
        ReadOnlySpan<byte> target, bool firstWindow, IBufferWriter<byte>? destination)
    {
        // Finish already compares a lone ADD or RUN with a literal-only window.
        if (original.IsEmptyOrSingleDataOperation)
            return original.Finish(target, firstWindow, destination);

        // Each default opcode contains at most one COPY or RUN. ADDs are implicit gaps.
        int capacity = Math.Min(original.Instructions.Length, target.Length);
        CopyMatch[]? rented = null;
        Span<CopyMatch> operations = capacity <= OperationScratch ? stackalloc CopyMatch[capacity] :
            (rented = ArrayPool<CopyMatch>.Shared.Rent(capacity)).AsSpan(0, capacity);
        try
        {
            int count = ReadOperations(original.Instructions, original.Addresses, source.Length, operations);
            if (count == 0) return original.Finish(target, firstWindow, destination);
            operations = ExtendBackward(operations[..count], source, target);
            ExtendForward(operations, source, target);

            // The refined window declares only the part of the source its copies still read.
            var (dictionaryStart, dictionarySize) = SourceRange(operations, source.Length);
            var candidate = new WindowWriter(dictionarySize == 0 ? 0 : sourceOffset + dictionaryStart, dictionarySize,
                stackalloc byte[WindowEncoder.DataScratchSize], stackalloc byte[WindowEncoder.InstructionScratchSize],
                stackalloc byte[WindowEncoder.AddressScratchSize], stackalloc int[AddressCache.SameSize]);
            try
            {
                Write(ref candidate, operations, source.Length, dictionaryStart, dictionarySize, target);
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

    private static (int Start, int Length) SourceRange(ReadOnlySpan<CopyMatch> operations, int sourceLength)
    {
        int start = sourceLength, end = 0;
        foreach (var op in operations)
        {
            if (op.IsRun || op.ReadOffset >= sourceLength) continue;
            start = Math.Min(start, op.ReadOffset);
            end = Math.Max(end, op.ReadOffset + op.Length);
        }
        return end == 0 ? (0, 0) : (start, end - start);
    }

    private static void Write(ref WindowWriter writer, ReadOnlySpan<CopyMatch> operations, int sourceLength,
        int dictionaryStart, int dictionarySize, ReadOnlySpan<byte> target)
    {
        int written = 0;
        foreach (var op in operations)
        {
            if (op.IsRun)
            {
                if (op.Length <= MaxLengthToPrice && writer.RunSavings(op.WriteOffset, op.Length) <= 0) continue;
                writer.Add(target[written..op.WriteOffset]);
                writer.Run(target[op.WriteOffset], op.Length);
            }
            else
            {
                int address = op.ReadOffset < sourceLength ? op.ReadOffset - dictionaryStart :
                    op.ReadOffset - sourceLength + dictionarySize;
                if (op.Length <= MaxLengthToPrice && writer.CopySavings(op.WriteOffset, address, op.Length) <= 0) continue;
                writer.Add(target[written..op.WriteOffset]);
                writer.Copy(address, op.Length);
            }
            written = op.End;
        }
        writer.Add(target[written..]);
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
            if (length < (op.IsRun ? MinRunLength : MinCopyLength)) continue;
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
