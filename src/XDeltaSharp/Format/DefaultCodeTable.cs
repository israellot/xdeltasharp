namespace XDeltaSharp.Format;

internal enum InstructionKind : byte { NoOp, Add, Run, Copy }

internal readonly record struct Instruction(InstructionKind Kind, byte Size = 0, byte Mode = 0);

/// <summary>The 256 entries of the RFC 3284 section 5.6 default table, in wire order.</summary>
internal static class DefaultCodeTable
{
    internal const int MaxFixedAddSize = 17;
    internal const int MinFixedCopySize = 4;
    internal const int MaxFixedCopySize = 18;

    // Paired opcodes combine an ADD of 1 to 4 bytes with a short COPY. Section 5.6 assigns 163-234 to
    // ADD+COPY with SELF, HERE and NEAR, 235-246 to ADD+COPY with SAME, and 247-255 to COPY+ADD.
    internal const int MaxPairedAddSize = 4;
    private const int MaxPairedCopySize = 6;
    private const int PairedCopySizes = MaxPairedCopySize - MinFixedCopySize + 1;
    private const int FirstAddCopyOpcode = 163;
    private const int FirstAddSameCopyOpcode = 235;
    private const int FirstCopyAddOpcode = 247;

    private static readonly (Instruction First, Instruction Second)[] Entries = Build();

    internal static (Instruction First, Instruction Second) Lookup(byte opcode) => Entries[opcode];

    internal static byte AddOpcode(int length) => length <= MaxFixedAddSize ? (byte)(1 + length) : (byte)1;

    internal static bool HasFixedCopySize(int length) => length is >= MinFixedCopySize and <= MaxFixedCopySize;

    internal static byte CopyOpcode(int mode, int length)
    {
        // Each mode has one variable-size COPY followed by fixed sizes 4 through 18.
        const int firstCopy = MaxFixedAddSize + 2;
        const int opcodesPerMode = MaxFixedCopySize - MinFixedCopySize + 2;
        int sizeOffset = HasFixedCopySize(length) ? length - MinFixedCopySize + 1 : 0;
        return (byte)(firstCopy + opcodesPerMode * mode + sizeOffset);
    }

    internal static bool TryPair(byte first, byte second, out byte opcode)
    {
        var left = Entries[first].First;
        var right = Entries[second].First;
        if (left.Kind == InstructionKind.Add && left.Size is >= 1 and <= MaxPairedAddSize && right.Kind == InstructionKind.Copy)
        {
            if (right.Mode < AddressCache.FirstSameMode && right.Size is >= MinFixedCopySize and <= MaxPairedCopySize)
            {
                opcode = (byte)(FirstAddCopyOpcode + right.Mode * MaxPairedAddSize * PairedCopySizes
                    + (left.Size - 1) * PairedCopySizes + right.Size - MinFixedCopySize);
                return true;
            }
            if (right.Mode >= AddressCache.FirstSameMode && right.Size == MinFixedCopySize)
            {
                opcode = (byte)(FirstAddSameCopyOpcode + (right.Mode - AddressCache.FirstSameMode) * MaxPairedAddSize
                    + left.Size - 1);
                return true;
            }
        }
        if (left.Kind == InstructionKind.Copy && left.Size == MinFixedCopySize && right.Kind == InstructionKind.Add && right.Size == 1)
        {
            opcode = (byte)(FirstCopyAddOpcode + left.Mode);
            return true;
        }
        opcode = 0;
        return false;
    }

    private static (Instruction, Instruction)[] Build()
    {
        var table = new (Instruction, Instruction)[256];
        int opcode = 0;
        table[opcode++] = (new(InstructionKind.Run), default);
        for (byte size = 0; size <= MaxFixedAddSize; size++)
            table[opcode++] = (new(InstructionKind.Add, size), default);

        for (byte mode = 0; mode < AddressCache.ModeCount; mode++)
        {
            table[opcode++] = (new(InstructionKind.Copy, 0, mode), default);
            for (byte size = MinFixedCopySize; size <= MaxFixedCopySize; size++)
                table[opcode++] = (new(InstructionKind.Copy, size, mode), default);
        }

        for (byte mode = 0; mode < AddressCache.ModeCount; mode++)
        {
            int maxCopySize = mode < AddressCache.FirstSameMode ? MaxPairedCopySize : MinFixedCopySize;
            for (byte add = 1; add <= MaxPairedAddSize; add++)
                for (byte copy = MinFixedCopySize; copy <= maxCopySize; copy++)
                    table[opcode++] = (new(InstructionKind.Add, add), new(InstructionKind.Copy, copy, mode));
        }

        for (byte mode = 0; mode < AddressCache.ModeCount; mode++)
            table[opcode++] = (new(InstructionKind.Copy, MinFixedCopySize, mode), new(InstructionKind.Add, 1));

        return table;
    }
}
