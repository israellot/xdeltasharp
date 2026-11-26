using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using XDeltaSharp.Format;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class WindowWriterTests
{
    [Fact]
    public void WriterEmitsAllAddressModesAndPairedOpcodes()
    {
        var writer = new WindowWriter(0, 16388, stackalloc byte[64], stackalloc byte[128],
            stackalloc byte[128], stackalloc int[768]);
        try
        {
            int[] offsets = [256, 800, 1360, 1900, 260, 804, 1364, 1904, 256, 800, 1360, 0, 16384];
            byte[] source = new byte[16388];
            new Random(17).NextBytes(source);
            var target = new List<byte>();
            foreach (int offset in offsets)
            {
                writer.Add("!"u8);
                writer.Copy(offset, 4);
                target.Add((byte)'!');
                target.AddRange(source[offset..(offset + 4)]);
            }
            byte[] expected = target.ToArray();
            byte[] patch = writer.Finish(expected, true, null)!;
            AssertDecodes(source, patch, expected);
            var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
            Assert.True(reader.TryReadWindow(source.Length, 0, expected.Length, out var window));
            var modes = new HashSet<byte>();
            foreach (byte opcode in window.Instructions)
            {
                var (first, second) = DefaultCodeTable.Lookup(opcode);
                Assert.Equal(InstructionKind.Add, first.Kind);
                Assert.Equal(InstructionKind.Copy, second.Kind);
                modes.Add(second.Mode);
            }
            Assert.Equal(9, modes.Count);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void WriterPairsCopyThenAddAndSupportsOverlappingTargetAddresses()
    {
        var writer = new WindowWriter(0, 0, stackalloc byte[16], stackalloc byte[16], stackalloc byte[16], []);
        try
        {
            writer.Add("ABCD"u8);
            writer.Copy(0, 16);
            writer.Copy(0, 4);
            writer.Add("!"u8);
            byte[] target = "ABCDABCDABCDABCDABCDABCD!"u8.ToArray();
            byte[] patch = writer.Finish(target, true, null)!;
            AssertDecodes([], patch, target);
            var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
            Assert.True(reader.TryReadWindow(0, 0, target.Length, out var window));
            Assert.Equal(247, window.Instructions[^1]);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void AllDefaultInstructionPairsCanBeWritten()
    {
        for (int opcode = 163; opcode <= 255; opcode++)
        {
            var (a, b) = DefaultCodeTable.Lookup((byte)opcode);
            byte FirstOpcode(Instruction instruction) => (byte)Enumerable.Range(0, 163)
                .Single(i => DefaultCodeTable.Lookup((byte)i).First == instruction);
            Assert.True(DefaultCodeTable.TryPair(FirstOpcode(a), FirstOpcode(b), out var actual));
            Assert.Equal(opcode, actual);
        }
    }
}
