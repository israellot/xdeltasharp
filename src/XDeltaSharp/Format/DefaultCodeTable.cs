namespace XDeltaSharp.Format;

internal enum InstructionKind : byte { NoOp, Add, Run, Copy }

internal readonly record struct Instruction(InstructionKind Kind, byte Size = 0, byte Mode = 0);

/// <summary>The 256 entries of the RFC 3284 section 5.6 default table, in wire order.</summary>
internal static class DefaultCodeTable
{
    internal const int MaxFixedAddSize = 17;
    internal const int MinFixedCopySize = 4;
    internal const int MaxFixedCopySize = 18;

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
        // RFC 3284 §5.6 assigns 163–234 to ADD+COPY with SELF/HERE/NEAR,
        // 235–246 to ADD+COPY with SAME, and 247–255 to COPY+ADD in any mode.
        var a = Entries[first].First;
        var b = Entries[second].First;
        if (a.Kind == InstructionKind.Add && a.Size is >= 1 and <= 4 && b.Kind == InstructionKind.Copy)
        {
            if (b.Mode < AddressCache.FirstSameMode && b.Size is >= 4 and <= 6)
            {
                opcode = (byte)(163 + b.Mode * 12 + (a.Size - 1) * 3 + b.Size - 4);
                return true;
            }
            if (b.Mode >= AddressCache.FirstSameMode && b.Size == 4)
            {
                opcode = (byte)(235 + (b.Mode - AddressCache.FirstSameMode) * 4 + a.Size - 1);
                return true;
            }
        }
        if (a.Kind == InstructionKind.Copy && a.Size == 4 && b.Kind == InstructionKind.Add && b.Size == 1)
        {
            opcode = (byte)(247 + a.Mode);
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
            int maxCopySize = mode < AddressCache.FirstSameMode ? 6 : 4;
            for (byte add = 1; add <= 4; add++)
                for (byte copy = 4; copy <= maxCopySize; copy++)
                    table[opcode++] = (new(InstructionKind.Add, add), new(InstructionKind.Copy, copy, mode));
        }

        for (byte mode = 0; mode < AddressCache.ModeCount; mode++)
            table[opcode++] = (new(InstructionKind.Copy, 4, mode), new(InstructionKind.Add, 1));

        return table;
    }
}
