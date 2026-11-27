using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class WindowOptimizerTests
{
    [Fact]
    public void BackwardExtensionStopsAtEveryMismatchAndAtTheShorterSpan()
    {
        byte[] left = new byte[73], right = new byte[91];
        int limit = left.Length;
        for (int suffix = 0; suffix < limit; suffix++)
        {
            right[^(suffix + 1)] = 1;
            Assert.Equal(suffix, CopyMatch.CommonSuffixLength(left, right));
            Assert.Equal(suffix, CopyMatch.CommonSuffixLength(right, left));
            right[^(suffix + 1)] = 0;
        }
        Assert.Equal(limit, CopyMatch.CommonSuffixLength(left, right));
        Assert.Equal(0, CopyMatch.CommonSuffixLength(left, []));
    }

    [Fact]
    public void MergesSourceCopiesAndRebasesAnOverlappingTargetCopy()
    {
        var source = new byte[32768];
        new Random(717).NextBytes(source);
        byte[] word = source[20000..20016];
        byte[] target = Enumerable.Repeat(word, 16).SelectMany(w => w).ToArray();
        var writer = new WindowWriter(0, source.Length, stackalloc byte[32], stackalloc byte[32],
            stackalloc byte[32], []);
        try
        {
            writer.Copy(20000, 4);
            writer.Copy(20004, 12);
            writer.Copy(source.Length, 240);
            byte[] original = writer.Finish(target, true, null)!;
            byte[] smaller = WindowOptimizer.Finish(ref writer, source, 0, target, true, null)!;
            Assert.True(smaller.Length < original.Length);
            var reader = new WindowReader(smaller, Vcdiff.MaxTargetWindowBytes);
            Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
            Assert.Equal(20000, window.SegmentOffset);
            Assert.Equal(16, window.SegmentLength);
            AssertDecodes(source, smaller, target);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void CanOmitAnUnusedDictionaryEvenWhenCopyUsesTargetHistory()
    {
        var source = new byte[32000];
        byte[] target = Enumerable.Repeat("ABCD"u8.ToArray(), 40).SelectMany(x => x).ToArray();
        var writer = new WindowWriter(1000, source.Length, stackalloc byte[32], stackalloc byte[32], stackalloc byte[32], []);
        try
        {
            writer.Add(target.AsSpan(0, 4));
            writer.Copy(source.Length, target.Length - 4);
            byte[] smaller = WindowOptimizer.Finish(ref writer, source, 1000, target, true, null)!;
            Assert.Equal(0, smaller[5]);
            AssertDecodes([], smaller, target);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void RunAbsorbsEqualLiteralsOnBothSides()
    {
        byte[] target = "XaaaaaabbbbbbbbbbbbbbbY"u8.ToArray();
        var writer = new WindowWriter(0, 0, stackalloc byte[32], stackalloc byte[32], stackalloc byte[32], []);
        try
        {
            writer.Add(target.AsSpan(0, 2));
            writer.Run((byte)'a', 3);
            writer.Add(target.AsSpan(5, 4));
            writer.Run((byte)'b', target.Length - 11);
            writer.Add(target.AsSpan(^2));
            byte[] original = writer.Finish(target, true, null)!;
            byte[] smaller = WindowOptimizer.Finish(ref writer, [], 0, target, true, null)!;
            Assert.True(smaller.Length < original.Length);
            AssertDecodes([], smaller, target);
        }
        finally { writer.Dispose(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(32768)]
    public void TiesKeepTheOriginalFrame(int size)
    {
        var target = new byte[size];
        byte[] expected = Vcdiff.Encode([], target);
        var output = new ArrayBufferWriter<byte>();
        Vcdiff.EncodeOptimized([], target, output);
        Assert.True(output.WrittenSpan.SequenceEqual(expected));
        Assert.Equal(expected, Vcdiff.EncodeOptimized([], target));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16384)]
    public void AWholeWindowRunCanFinishDirectlyIncludingItsLiteralFallback(int size)
    {
        var target = new byte[size];
        var writer = new WindowWriter(20, 10, stackalloc byte[8], stackalloc byte[8], stackalloc byte[8], []);
        try
        {
            writer.Run(0, size);
            Assert.True(writer.IsEmptyOrSingleDataOperation);
            Assert.Equal(size <= 2, writer.Measure(size).LiteralOnly);

            byte[] patch = writer.Finish(target, true, null)!;
            Assert.Equal(patch, WindowOptimizer.Finish(ref writer, new byte[10], 20, target, true, null));
            AssertDecodes([], patch, target);

            writer.Run(1, 1);
            Assert.False(writer.IsEmptyOrSingleDataOperation);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void MixedOperationsStillRequireRefinement()
    {
        var writer = new WindowWriter(0, 4, stackalloc byte[8], stackalloc byte[8], stackalloc byte[8], []);
        try
        {
            Assert.True(writer.IsEmptyOrSingleDataOperation);
            writer.Add("a variable-size literal"u8);
            Assert.True(writer.IsEmptyOrSingleDataOperation);
            writer.Run(0, 1);
            Assert.False(writer.IsEmptyOrSingleDataOperation);
        }
        finally { writer.Dispose(); }

        var copy = new WindowWriter(0, 4, stackalloc byte[8], stackalloc byte[8], stackalloc byte[8], []);
        try
        {
            copy.Copy(0, 4);
            Assert.False(copy.IsEmptyOrSingleDataOperation);
        }
        finally { copy.Dispose(); }
    }

    [Fact]
    public void KeepsOriginalWhenASmallerDictionaryMakesTheHeaderLarger()
    {
        byte[] source = new byte[147];
        new Random(41).NextBytes(source);
        var segment = source.AsSpan(127);
        var target = segment.Slice(3, 16).ToArray();
        var writer = new WindowWriter(127, 20, stackalloc byte[16], stackalloc byte[16], stackalloc byte[16], []);
        try
        {
            writer.Copy(3, 16);
            var original = writer.Finish(target, true, null)!;
            // Rebasing to file offset 130 adds a varint byte. The two COPY addresses both use one byte.
            Assert.Equal(original, WindowOptimizer.Finish(ref writer, segment, 127, target, true, null));
            AssertDecodes(source, original, target);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Allocation")]
    public void SmallOptimizedWriterCallsDoNotAllocateAfterWarmup()
    {
        var source = "abcdefghabcdefghZYXWVUTS12345678"u8.ToArray();
        byte[] target = [.. source, 1, .. source[..12]];
        var writer = new ArrayBufferWriter<byte>(128);
        Vcdiff.EncodeOptimized(source, target, writer);
        long first = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            writer.ResetWrittenCount();
            Vcdiff.EncodeOptimized(source, target, writer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - first;
        Assert.Equal(0, allocated);
        AssertDecodes(source, writer.WrittenSpan.ToArray(), target);
    }

    [Fact]
    public void NeverGrowsPatchesAcrossLevelsAndMemoryPolicies()
    {
        var random = new Random(941);
        for (int trial = 0; trial < 20; trial++)
        {
            byte[] source = new byte[random.Next(500, 4200)];
            random.NextBytes(source);
            var buffer = new List<byte>();
            for (int i = 0; i < 70; i++)
            {
                int pos = random.Next(source.Length - 48);
                buffer.AddRange(source.AsSpan(pos, random.Next(4, 48)).ToArray());
                int old = random.Next(buffer.Count);
                buffer.AddRange(buffer.GetRange(old, Math.Min(random.Next(4, 48), buffer.Count - old)));
                buffer.AddRange(Enumerable.Repeat((byte)random.Next(8), random.Next(20)));
            }
            byte[] target = buffer.ToArray();
            foreach (var memory in Enum.GetValues<MemoryMode>())
            {
                for (int level = 0; level <= 9; level++)
                {
                    var original = Vcdiff.Encode(source, target, level, memory);
                    var optimized = Vcdiff.EncodeOptimized(source, target, level, memory);
                    var sink = new ArrayBufferWriter<byte>();
                    sink.Write("start"u8);
                    Vcdiff.EncodeOptimized(source, target, sink, level, memory);
                    Assert.True(sink.WrittenSpan[..5].SequenceEqual("start"u8));
                    Assert.Equal(optimized, sink.WrittenSpan[5..].ToArray());
                    Verify(source, target, original, optimized);
                }
            }
        }
    }

    private static void Verify(byte[] source, byte[] target, byte[] original, byte[] optimized)
    {
        Assert.True(optimized.Length <= original.Length);
        if (optimized.Length == original.Length) Assert.Equal(original, optimized);
        Assert.Equal(target, Vcdiff.Decode(source, optimized));
    }

    [Fact]
    public void SizeGuardAppliesSeparatelyToEveryWindow()
    {
        const int windowSize = 8 << 20;
        byte[] target = new byte[windowSize + 101];
        for (int i = 0; i < target.Length; i++) target[i] = (byte)(i % 5);
        byte[] source = target[..1024];
        byte[] original = Vcdiff.Encode(source, target);
        byte[] optimized = Vcdiff.EncodeOptimized(source, target);
        Verify(source, target, original, optimized);
        using var first = new MemoryStream(original);
        using var second = new MemoryStream(optimized);
        var a = new WindowReader(first, Vcdiff.MaxTargetWindowBytes, Array.MaxLength);
        var b = new WindowReader(second, Vcdiff.MaxTargetWindowBytes, Array.MaxLength);
        foreach (int expected in new[] { windowSize, 101 })
        {
            long startA = first.Position, startB = second.Position;
            Assert.True(a.TryReadWindow(source.Length, 0, expected, out var x));
            Assert.True(b.TryReadWindow(source.Length, 0, expected, out var y));
            Assert.Equal(expected, x.TargetLength);
            Assert.Equal(expected, y.TargetLength);
            Assert.True(second.Position - startB <= first.Position - startA);
        }
    }
}
