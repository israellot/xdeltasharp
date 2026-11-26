namespace XDeltaSharp.Format;

internal enum InstructionKind : byte { NoOp, Add, Run, Copy }

internal readonly record struct Instruction(InstructionKind Kind, byte Size = 0, byte Mode = 0);

/// <summary>The 256 entries of the RFC 3284 section 5.6 default table, in wire order.</summary>
internal static class DefaultCodeTable
{
    private static readonly (Instruction First, Instruction Second)[] Entries = Build();

    internal static (Instruction First, Instruction Second) Lookup(byte opcode) => Entries[opcode];

    internal static bool TryPair(byte first, byte second, out byte opcode)
    {
        var a = Entries[first].First;
        var b = Entries[second].First;
        if (a.Kind == InstructionKind.Add && a.Size is >= 1 and <= 4 && b.Kind == InstructionKind.Copy)
        {
            if (b.Mode < 6 && b.Size is >= 4 and <= 6)
            {
                opcode = (byte)(163 + b.Mode * 12 + (a.Size - 1) * 3 + b.Size - 4);
                return true;
            }
            if (b.Mode >= 6 && b.Size == 4)
            {
                opcode = (byte)(235 + (b.Mode - 6) * 4 + a.Size - 1);
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
        for (byte size = 0; size <= 17; size++)
            table[opcode++] = (new(InstructionKind.Add, size), default);

        for (byte mode = 0; mode < 9; mode++)
        {
            table[opcode++] = (new(InstructionKind.Copy, 0, mode), default);
            for (byte size = 4; size <= 18; size++)
                table[opcode++] = (new(InstructionKind.Copy, size, mode), default);
        }

        for (byte mode = 0; mode < 9; mode++)
        {
            int maxCopySize = mode < 6 ? 6 : 4;
            for (byte add = 1; add <= 4; add++)
                for (byte copy = 4; copy <= maxCopySize; copy++)
                    table[opcode++] = (new(InstructionKind.Add, add), new(InstructionKind.Copy, copy, mode));
        }

        for (byte mode = 0; mode < 9; mode++)
            table[opcode++] = (new(InstructionKind.Copy, 4, mode), new(InstructionKind.Add, 1));

        return table;
    }
}
