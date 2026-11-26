using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using XDeltaSharp.Format;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

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
    public void ShortCopyCanYieldToThePreviousAlignmentOneByteAhead()
    {
        byte[] source = Bytes(512);
        byte[] target = source[64..160];
        target[32] ^= 0xFF;
        target[33] ^= 0xFF;
        // The ordinary probe at 33 finds a real eight-byte COPY at an unrelated landmark.
        target.AsSpan(33, 8).CopyTo(source.AsSpan(256));
        source[255] = (byte)~target[32];
        source[264] = (byte)~target[41];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(target[32..34], window.Data.ToArray());
        Assert.Equal(new byte[] { 19, 32, 3, 51, 62 }, window.Instructions.ToArray());
        Assert.Equal(new byte[] { 64, 34 }, window.Addresses.ToArray());
    }

    [Fact]
    public void ResumesUnalignedCopiesAcrossIsolatedEdits()
    {
        byte[] source = Bytes(512);
        byte[] target = source[64..(64 + 60)];
        int[] edits = [15, 30, 45];
        foreach (int offset in edits) target[offset] ^= 255;
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(edits.Select(i => target[i]).ToArray(), window.Data.ToArray());
        Assert.Equal(4, window.Addresses.Length);
    }

    [Fact]
    public void APartialSourceContinuationDoesNotSplitALongerRun()
    {
        byte[] source = Bytes(512);
        source[64] = 1;
        source.AsSpan(65, 16).Clear();
        source[81] = 1;
        byte[] target = [.. source[..64], .. new byte[256]];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(new byte[] { 0 }, window.Data.ToArray());
        byte[] instructions = [19, 64, 0, .. Integer(256)];
        Assert.Equal(instructions, window.Instructions.ToArray());
    }

    [Theory]
    [InlineData(MemoryMode.Default)]
    [InlineData(MemoryMode.LessMemory)]
    public void WindowSamplingCanRecognizeAReusedRegionAwayFromTheStart(MemoryMode memory)
    {
        byte[] source = Bytes(4096);
        byte[] target = new byte[1024];
        using var finder = new SourceMatchFinder(source, target.Length, memory, Span<byte>.Empty);
        Assert.False(finder.ShouldUseCopyPrediction(target, 0));
        source.AsSpan(0, 48).CopyTo(target.AsSpan(target.Length / 2));
        Assert.True(finder.ShouldUseCopyPrediction(target, 0));
    }

    [Fact]
    public void HandlesUnalignedSourceAndBackwardExtension()
    {
        byte[] source = Bytes(1024);
        byte[] target = [123, .. source[3..95], 21];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
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
        AssertPatch(source, target, Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(524288)]
    public void LaterDuplicateSeedCanWinAtEveryIndexWidth(int sourceLength)
    {
        byte[] source = Bytes(sourceLength);
        source.AsSpan(0, 8).CopyTo(source.AsSpan(128));
        byte[] target = source[128..392];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
        AssertDecodes(source, patch, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(Integer(128), window.Addresses.ToArray());
        byte[] instructions = [19, .. Integer(target.Length)];
        Assert.Equal(instructions, window.Instructions.ToArray());
    }

    [Theory]
    [InlineData(256, 64, MemoryMode.Default)]
    [InlineData(257, 8, MemoryMode.Default)]
    [InlineData(256, 64, MemoryMode.LessMemory)]
    [InlineData(257, 8, MemoryMode.LessMemory)]
    public void SearchStopsAfter256LinksFromTheBucketHead(int winningHop, int expectedLength, MemoryMode mode)
    {
        byte[] target = Bytes(64);
        byte[] source = new byte[winningHop * 8 + target.Length];
        for (int offset = 0; offset < winningHop * 8; offset += 8)
            target.AsSpan(0, 8).CopyTo(source.AsSpan(offset));
        target.CopyTo(source, winningHop * 8);

        using var finder = new SourceMatchFinder(source, target.Length, mode, Span<byte>.Empty);
        var match = finder.FindMatch(target, 0, 0);
        Assert.Equal(expectedLength, match.Length);
        Assert.Equal(expectedLength == target.Length ? winningHop * 8 : 0, match.ReadOffset);
    }

    [Fact]
    public void PackedLinksSummarizeExactlyTheirOwnRemainingByteClasses()
    {
        using var index = new MatchIndex(65536, 1, []);
        index.PrependWithMask(0, 65535, 0xF9);
        index.PrependWithMask(0, 3, 0x09); // Same class as 0xF9.
        index.PrependWithMask(0, 0, 0xFF); // Class seven occupies the sign bit.

        Assert.Equal(0x8200_0001u, unchecked((uint)index.Entry(0)));
        Assert.Equal(0x0200_0004, index.Entry(1));
        Assert.Equal(0x0201_0000, index.Entry(1 + 3));
        Assert.Equal(0, index.Entry(1 + 65535));
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void PackedFastTablePreservesHopLimitAndFirstTie(int winningHop, bool reachable)
    {
        byte[] source = new byte[524288];
        byte[] target = Bytes(320);
        target[0] = 0xFF;
        for (int i = 0; i < winningHop; i++)
            target.AsSpan(0, 8).CopyTo(source.AsSpan(i * 8));
        target.CopyTo(source, winningHop * 8);

        foreach (var memory in Enum.GetValues<MemoryMode>())
        {
            using var finder = new SourceMatchFinder(source, target.Length, memory, []);
            CopyMatch expected = reachable ? new(winningHop * 8, 0, target.Length) : new(0, 0, 8);
            Assert.Equal(expected, finder.FindMatch(target, 0, 0));
        }
    }

    [Fact]
    public void UnequalSeedsInTheSameBucketDoNotHideALaterMatch()
    {
        byte[] source = Bytes(128);
        // Little-endian seeds 143 and 0 both hash to byte-table bucket zero.
        source.AsSpan(0, 8).Clear();
        source[0] = 143;
        source.AsSpan(32, 8).Clear();
        byte[] target = source[32..96];
        using var finder = new SourceMatchFinder(source, target.Length, MemoryMode.Default, stackalloc byte[768]);
        Assert.Equal(new CopyMatch(32, 0, 64), finder.FindMatch(target, 0, 0));
    }

    [Fact]
    public void RanksCandidatesIncludingExtensionIntoPendingLiterals()
    {
        byte[] target = Bytes(64);
        byte[] source = new byte[128];
        target.AsSpan(8, 10).CopyTo(source.AsSpan(32));
        source[31] = (byte)~target[7];
        source[42] = (byte)~target[18];
        target.AsSpan(0, 16).CopyTo(source.AsSpan(88));
        source[104] = (byte)~target[16];
        using var finder = new SourceMatchFinder(source, target.Length, MemoryMode.Default, stackalloc byte[768]);
        Assert.Equal(new CopyMatch(91, 3, 13), finder.FindMatch(target, probe: 8, pendingStart: 3));
        Assert.Equal(new CopyMatch(32, 8, 10), finder.FindMatch(target, probe: 8, pendingStart: 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void SplitsAtEightMiBAndRestartsEachWindow(int extra)
    {
        const int size = 8 << 20;
        byte[] target = new byte[size + extra];
        byte[] patch = Vcdiff.Encode([], target, VcdiffEncodeMode.Fast);
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
            AssertPatch(source, bytes, Vcdiff.Encode(source, bytes, VcdiffEncodeMode.Fast));
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

    [Theory]
    [InlineData(16, MemoryMode.Default)]
    [InlineData(64, MemoryMode.Default)]
    [InlineData(16, MemoryMode.LessMemory)]
    [InlineData(64, MemoryMode.LessMemory)]
    public void SmallCopyEncodingOnlyAllocatesTheReturnedArray(int copyLength, MemoryMode memoryMode)
    {
        byte[] source = Bytes(128);
        byte[] target = [21, .. source.AsSpan(8, copyLength), 42];
        var sink = new ArrayBufferWriter<byte>(128);
        byte[] expected = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast, memoryMode);
        Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast, memoryMode);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            sink.ResetWrittenCount();
            Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast, memoryMode);
        }
        long writerBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) GC.KeepAlive(Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast, memoryMode));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Encode([], [], (MemoryMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Encode([], [], sink, (MemoryMode)100));
        Assert.Throws<ArgumentNullException>(() => Vcdiff.Encode([], [], null!));
        Assert.Equal(0, sink.WrittenCount);
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(79, 16)]
    [InlineData(2047, 256)]
    [InlineData(2048, 256)]
    [InlineData(2056, 512)]
    [InlineData(524287, 65536)]
    [InlineData(524288, 65536)]
    public void BucketCountScalesAcrossEntryWidths(int sourceLength, int roundedBlocks)
    {
        byte[] source = Bytes(sourceLength);
        const int targetLength = 300;
        foreach (var mode in new[] { MemoryMode.Default, MemoryMode.LessMemory })
        {
            int buckets = mode == MemoryMode.Default ? 2 * roundedBlocks : roundedBlocks;
            int scratch = SourceMatchFinder.SmallTableSize(sourceLength, targetLength, mode);
            Assert.Equal(sourceLength / 8 <= byte.MaxValue ? buckets + sourceLength / 8 : 0, scratch);
            using var finder = new SourceMatchFinder(source, targetLength, mode, new byte[scratch]);
            Assert.Equal(buckets, finder.BucketCount);
        }
    }

    [Fact]
    public void MemoryModeControlsBothOutputPathsAndDefaultsToTheLargerTable()
    {
        // Seeds 1164 and 0 have the same low 9 hash bits and different tenth bits.
        // With 512 buckets the zero seed is behind 257 collisions. With 1024 it is at the head.
        byte[] source = new byte[4096];
        for (int block = 0; block < 257; block++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(source.AsSpan(8 * block), 1164);
        byte[] target = Bytes(64);
        target.AsSpan(0, 8).Clear();
        target.CopyTo(source, 257 * 8);

        var larger = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast, MemoryMode.Default);
        var smaller = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast, MemoryMode.LessMemory);
        Assert.True(larger.Length < smaller.Length);
        Assert.Equal(larger, Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast));
        AssertDecodes(source, larger, target);
        AssertDecodes(source, smaller, target);
        var sink = new ArrayBufferWriter<byte>();
        Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast);
        Assert.Equal(larger, sink.WrittenSpan.ToArray());
        sink.ResetWrittenCount();
        Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast, memoryMode: MemoryMode.LessMemory);
        Assert.Equal(smaller, sink.WrittenSpan.ToArray());
        sink.ResetWrittenCount();
        Vcdiff.Encode(source, target, sink, VcdiffEncodeMode.Fast, MemoryMode.LessMemory);
        Assert.Equal(Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast, MemoryMode.LessMemory), sink.WrittenSpan.ToArray());
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
