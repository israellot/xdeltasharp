using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class CompressionLevelTests
{
    [Theory]
    [InlineData(0, 26, 0, 0, 6, 6)]
    [InlineData(1, 26, 1, 1, 6, 6)]
    [InlineData(2, 15, 1, 1, 18, 18)]
    [InlineData(3, 8, 4, 1, 18, 35)]
    [InlineData(4, 8, 4, 1, 18, 35)]
    [InlineData(5, 8, 4, 1, 18, 35)]
    [InlineData(6, 3, 8, 2, 36, 70)]
    [InlineData(7, 2, 44, 13, 90, 70)]
    [InlineData(8, 2, 44, 13, 90, 70)]
    [InlineData(9, 2, 44, 13, 90, 70)]
    public void UsesXdeltaCliMatcherProfiles(int level, int sourceStep, int targetCandidates, int lazyCandidates,
        int lazyMatchLimit, int targetStopLength)
        => Assert.Equal(new MatcherProfile(sourceStep, targetCandidates, lazyCandidates, lazyMatchLimit, targetStopLength),
            MatcherProfile.FromLevel(level));

    [Theory]
    [InlineData(MemoryMode.Default)]
    [InlineData(MemoryMode.LessMemory)]
    public void OmittedLevelIsLevelThree(MemoryMode memory)
    {
        // Reversing short records makes the different source search strides produce different patches.
        byte[] source = new byte[4096];
        new Random(17).NextBytes(source);
        byte[] target = new byte[source.Length];
        for (int offset = 0; offset < target.Length; offset += 16)
            source.AsSpan(source.Length - offset - 16, 16).CopyTo(target.AsSpan(offset));

        byte[] levelThree = Vcdiff.Encode(source, target, Level(3, memory));
        Assert.NotEqual(levelThree, Vcdiff.Encode(source, target, Level(2, memory)));
        Assert.Equal(levelThree, Vcdiff.Encode(source, target, new VcdiffEncodeOptions { MemoryMode = memory }));
        var sink = new ArrayBufferWriter<byte>();
        Vcdiff.Encode(source, target, sink, new VcdiffEncodeOptions { MemoryMode = memory });
        Assert.True(sink.WrittenSpan.SequenceEqual(levelThree));

        if (memory == MemoryMode.Default)
        {
            Assert.Equal(levelThree, Vcdiff.Encode(source, target));
            sink.ResetWrittenCount();
            Vcdiff.Encode(source, target, sink);
            Assert.True(sink.WrittenSpan.SequenceEqual(levelThree));
        }
    }

    [Fact]
    public void LevelZeroUsesSourceCopiesAndRunsButNotTargetCopies()
    {
        byte[] target = Enumerable.Repeat("ABCD"u8.ToArray(), 256).SelectMany(bytes => bytes).ToArray();
        var literal = Vcdiff.Encode([], target, Level(0));
        Assert.True(literal.Length > target.Length);
        Assert.True(Vcdiff.Encode([], target, Level(1)).Length < 32);
        AssertDecodes([], literal, target);
        var copy = Vcdiff.Encode(target, target, Level(0));
        var reader = new WindowReader(copy, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(target.Length, 0, target.Length, out var window));
        Assert.True(window.Data.IsEmpty);
        Assert.False(window.Addresses.IsEmpty);
        AssertDecodes(target, copy, target);
        byte[] run = new byte[4096];
        var patch = Vcdiff.Encode([], run, Level(0));
        Assert.True(patch.Length < 32);
        AssertDecodes([], patch, run);
    }

    [Theory]
    [InlineData(false, 0, 20)]
    [InlineData(true, 40, 4)]
    public void LevelThreeUsesDifferentNormalAndLazyChainBudgets(bool lazy, int offset, int length)
    {
        byte[] data = new byte[128];
        new Random(9).NextBytes(data);
        data.AsSpan(0, 4).CopyTo(data.AsSpan(40));
        data.AsSpan(0, 20).CopyTo(data.AsSpan(80));
        data[44] = (byte)~data[4];
        data[100] = (byte)~data[20];
        var history = new LevelTargetMatcher(data, MatcherProfile.FromLevel(3), MemoryMode.Default, stackalloc byte[512]);
        try
        {
            history.FindAndInsert(0, 4, false);
            history.FindAndInsert(40, 4, false);
            var found = history.FindAndInsert(80, 4, lazy);
            Assert.Equal(new CopyMatch(offset, 80, length), found);
        }
        finally { history.Dispose(); }
    }

    [Fact]
    public void HistoryKeepsOnlyVisitedSeedsAndBoundsOlderLinks()
    {
        const int ring = 1 << 18;
        byte[] data = new byte[ring + 100];
        new Random(192).NextBytes(data);
        data.AsSpan(0, 4).CopyTo(data.AsSpan(12));
        data[16] = (byte)~data[4];
        data.AsSpan(0, 32).CopyTo(data.AsSpan(ring + 20));
        data.AsSpan(64, 12).CopyTo(data.AsSpan(ring));
        var history = new LevelTargetMatcher(data, MatcherProfile.FromLevel(9), MemoryMode.Default, []);
        try
        {
            history.FindAndInsert(0, 4, false);
            history.FindAndInsert(12, 4, false);
            Assert.Equal(0, history.FindAndInsert(ring, 4, false).Length); // Position 64 was skipped.
            Assert.Equal(new CopyMatch(12, ring + 20, 4), history.FindAndInsert(ring + 20, 4, false));
        }
        finally { history.Dispose(); }
    }

    [Fact]
    public void LevelsRoundTripWithBothOutputAndMemoryPolicies()
    {
        var random = new Random(37);
        for (int trial = 0; trial < 18; trial++)
        {
            byte[] source = new byte[random.Next(40, 3000)];
            random.NextBytes(source);
            var data = new List<byte>();
            for (int i = 0; i < 80; i++)
            {
                int offset = random.Next(source.Length);
                data.AddRange(source.AsSpan(offset, random.Next(Math.Min(40, source.Length - offset) + 1)).ToArray());
                data.AddRange(Enumerable.Repeat((byte)random.Next(5), random.Next(14)));
                int earlier = random.Next(data.Count + 1);
                data.AddRange(data.GetRange(earlier, random.Next(Math.Min(32, data.Count - earlier) + 1)));
            }
            byte[] target = data.ToArray();
            foreach (var memory in new[] { MemoryMode.LessMemory, MemoryMode.Default })
            {
                var sink = new ArrayBufferWriter<byte>();
                byte[] previous = [];
                for (int level = 0; level <= 9; level++)
                {
                    byte[] patch = Vcdiff.Encode(source, target, Level(level, memory));
                    if (level is 4 or 5 or 8 or 9) Assert.Equal(previous, patch);
                    sink.ResetWrittenCount();
                    sink.Write("prefix"u8);
                    Vcdiff.Encode(source, target, sink, Level(level, memory));
                    Assert.Equal("prefix"u8.ToArray(), sink.WrittenSpan[..6].ToArray());
                    Assert.Equal(patch, sink.WrittenSpan[6..].ToArray());
                    Assert.Equal(target, Vcdiff.Decode(source, patch));
                    previous = patch;
                }
            }
        }
    }

    [Fact]
    public void FlushesABoundedInstructionBufferWithoutLosingOutput()
    {
        byte[] target = new byte[16 * 40000];
        new Random(74).NextBytes(target);
        for (int i = 0; i < target.Length; i += 16) target.AsSpan(i, 8).Fill((byte)(i / 16));
        byte[] patch = Vcdiff.Encode([], target, Level(0));
        Assert.True(patch.Length < target.Length);
        AssertDecodes([], patch, target);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void RejectsALevelOutsideZeroToNine(int level)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new VcdiffEncodeOptions { CompressionLevel = level });

    [Fact]
    public void RejectsAnUndefinedMemoryMode()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new VcdiffEncodeOptions { MemoryMode = (MemoryMode)100 });

    [Fact]
    public void RejectsANullWriterOrOptions()
    {
        var sink = new ArrayBufferWriter<byte>();
        Assert.Throws<ArgumentNullException>("destination", () => Vcdiff.Encode([], [], (IBufferWriter<byte>)null!));
        Assert.Throws<ArgumentNullException>("destination", () => Vcdiff.Encode([], [], null!, Level(1)));
        Assert.Throws<ArgumentNullException>("options", () => Vcdiff.Encode([], [], (VcdiffEncodeOptions)null!));
        Assert.Throws<ArgumentNullException>("options", () => Vcdiff.Encode([], [], sink, null!));
        Assert.Equal(0, sink.WrittenCount);
    }
}
