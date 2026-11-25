using System.Buffers;
using VCDiffSharp.Decoding;
using VCDiffSharp.Encoding;
using VCDiffSharp.Format;
using Xunit;
using static VCDiffSharp.Tests.WindowVectors;

namespace VCDiffSharp.Tests;

public class FastEncoderTests
{
    [Fact]
    public void EmitsBaselineWindowsForEmptyLiteralRunAndSourceCopy()
    {
        AssertPatch([], [], Patch(Window(0, [], [], [])));
        AssertPatch([], "abc"u8.ToArray(), Patch(Window(3, "abc"u8.ToArray(), [4], [])));
        AssertPatch([], "xxx"u8.ToArray(), Patch(Window(3, [(byte)'x'], [0, 3], [])));
        AssertPatch([], new byte[256], Patch(Window(256, [0], [0, .. Integer(256)], [])));
        byte[] source = "0123456789abcdef"u8.ToArray();
        AssertPatch(source, source, Patch(Window(16, [], [32], [0], 1, 16)));
    }

    [Fact]
    public void RepetitionsOnlyInTheTargetRemainLiterals()
    {
        byte[] target = Enumerable.Repeat("ABCD"u8.ToArray(), 64).SelectMany(x => x).ToArray();
        AssertPatch([], target, Patch(Window(256, target, [1, .. Integer(256)], [])));
    }

    [Fact]
    public void SmallCopyPaysForItsDictionaryHeaderOrFallsBackToAdd()
    {
        byte[] source = new byte[1500];
        "abcd"u8.CopyTo(source.AsSpan(1400));
        AssertPatch(source, "abcd"u8.ToArray(), Patch(Window(4, "abcd"u8.ToArray(), [5], [])));
    }

    [Fact]
    public void HandlesUnalignedSourceAndBackwardExtension()
    {
        byte[] source = Bytes(1024);
        byte[] target = [123, .. source[3..95], 21];
        byte[] patch = Vcdiff.Encode(source, target);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(new byte[] { 123, 21 }, window.Data.ToArray());
        Assert.Equal(new byte[] { 3 }, window.Addresses.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(32)]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(524287)]
    [InlineData(524288)]
    public void CompactTableBoundariesAndDeterministicSinkOutput(int length)
    {
        byte[] source = Bytes(length);
        byte[] target = [.. source.AsSpan(length / 2), 5, 6, 7, .. source.AsSpan(0, length / 2)];
        AssertPatch(source, target, Vcdiff.Encode(source, target));
    }

    [Fact]
    public void FastUsesOnlyTheFirstLandmarkForADuplicateSeed()
    {
        byte[] source = Bytes(256);
        source.AsSpan(0, 8).CopyTo(source.AsSpan(128));
        byte[] target = source[128..192];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        // The first seed also occurs at 128, where the entire target would match.
        Assert.Equal(0, window.Addresses[0]);
        Assert.Equal((byte)8, DefaultCodeTable.Lookup(window.Instructions[0]).First.Size);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void SplitsAtEightMiBAndRestartsEachWindow(int extra)
    {
        const int size = 8 << 20;
        byte[] target = new byte[size + extra];
        byte[] patch = Vcdiff.Encode([], target);
        int firstSize = Math.Min(size, target.Length);
        var first = Window(firstSize, [0], [0, .. Integer(firstSize)], []);
        Assert.Equal(extra > 0 ? Patch(first, Window(1, [0], [2], [])) : Patch(first), patch);
        Assert.Equal(target, Vcdiff.Decode([], patch));
    }

    [Fact]
    public void MixedOperationsAndPoolGrowthRoundTrip()
    {
        var random = new Random(57);
        for (int trial = 0; trial < 40; trial++)
        {
            byte[] source = new byte[random.Next(0, 6000)];
            random.NextBytes(source);
            var target = new List<byte>();
            for (int i = 0; i < 90; i++)
            {
                int start = random.Next(source.Length + 1);
                target.AddRange(source.AsSpan(start, random.Next(Math.Min(60, source.Length - start) + 1)).ToArray());
                target.AddRange(Enumerable.Repeat((byte)random.Next(256), random.Next(20)));
                target.Add((byte)random.Next(256));
            }
            byte[] bytes = target.ToArray();
            AssertPatch(source, bytes, Vcdiff.Encode(source, bytes));
        }
    }

    [Fact]
    public void WriterEmitsAllAddressModesAndPairedOpcodes()
    {
        var writer = new WindowWriter(0, 16388, stackalloc byte[64], stackalloc byte[128],
            stackalloc byte[128], stackalloc int[768]);
        try
        {
            int[] offsets = [256, 800, 1360, 1900, 260, 804, 1364, 1904, 256, 800, 1360, 0, 16384];
            byte[] source = Bytes(16388);
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

    [Fact]
    public void SmallCopyEncodingOnlyAllocatesTheReturnedArray()
    {
        byte[] source = Bytes(128);
        byte[] target = [21, .. source[8..24], 42];
        var sink = new ArrayBufferWriter<byte>(128);
        byte[] expected = Vcdiff.Encode(source, target);
        Vcdiff.Encode(source, target, sink);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            sink.ResetWrittenCount();
            Vcdiff.Encode(source, target, sink);
        }
        long writerBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) GC.KeepAlive(Vcdiff.Encode(source, target));
        long allocatingBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) GC.KeepAlive(new byte[expected.Length]);
        long outputArrayBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, writerBytes);
        Assert.Equal(outputArrayBytes, allocatingBytes);
        Assert.Equal(1, expected[5]); // A source COPY was actually used.
    }

    [Fact]
    public void InvalidModeAndNullWriterAreRejectedBeforeWriting()
    {
        var sink = new ArrayBufferWriter<byte>();
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Encode([], [], (VcdiffEncodeMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Encode([], [], sink, (VcdiffEncodeMode)100));
        Assert.Throws<ArgumentNullException>(() => Vcdiff.Encode([], [], null!));
        Assert.Equal(0, sink.WrittenCount);
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(17).NextBytes(bytes);
        return bytes;
    }

    private static void AssertPatch(byte[] source, byte[] target, byte[] expected)
    {
        Assert.Equal(expected, Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast));
        var sink = new ArrayBufferWriter<byte>();
        sink.Write("prefix"u8);
        Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast);
        Assert.Equal("prefix"u8.ToArray(), sink.WrittenSpan[..6].ToArray());
        Assert.Equal(expected, sink.WrittenSpan[6..].ToArray());
        AssertDecodes(source, expected, target);
    }
}
