using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class OpcodeTests
{
    // Independently expand the opcode ranges in RFC 3284 section 5.6.
    public static TheoryData<int> Opcodes => new(Enumerable.Range(0, 256));

    [Theory]
    [MemberData(nameof(Opcodes))]
    public void DecodesDefaultOpcode(int opcode)
    {
        byte[] dictionary = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8.ToArray();
        const int explicitSize = 19;
        int add = 0, copy = 0, run = 0, mode = 0;
        bool explicitLength = false;
        if (opcode == 0)
        {
            run = explicitSize;
            explicitLength = true;
        }
        else if (opcode <= 18)
        {
            explicitLength = opcode == 1;
            add = explicitLength ? explicitSize : opcode - 1;
        }
        else if (opcode <= 162)
        {
            mode = (opcode - 19) / 16;
            int withinMode = (opcode - 19) % 16;
            explicitLength = withinMode == 0;
            copy = explicitLength ? explicitSize : withinMode + 3;
        }
        else if (opcode <= 234)
        {
            mode = (opcode - 163) / 12;
            add = (opcode - 163) % 12 / 3 + 1;
            copy = (opcode - 163) % 3 + 4;
        }
        else if (opcode <= 246)
        {
            mode = 6 + (opcode - 235) / 4;
            add = (opcode - 235) % 4 + 1;
            copy = 4;
        }
        else
        {
            mode = opcode - 247;
            copy = 4;
            add = 1;
        }

        bool addFirst = opcode < 247;
        byte[] literals = Enumerable.Repeat((byte)'!', add).ToArray();
        byte[] copied = dictionary[..copy];
        byte[] target = run > 0 ? Enumerable.Repeat((byte)'?', run).ToArray()
            : addFirst ? [.. literals, .. copied] : [.. copied, .. literals];
        int here = dictionary.Length + (addFirst ? add : 0);
        byte[] addresses = copy == 0 ? [] : [mode == 1 ? (byte)here : (byte)0];
        byte[] instructions = explicitLength ? [(byte)opcode, explicitSize] : [(byte)opcode];
        byte[] data = run == 0 ? literals : [(byte)'?'];
        AssertDecodes(dictionary, Patch(Window(target.Length, data, instructions, addresses, 1, dictionary.Length)), target);
    }
}
