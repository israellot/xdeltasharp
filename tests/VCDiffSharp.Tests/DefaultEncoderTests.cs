using System.Buffers;
using VCDiffSharp.Decoding;
using VCDiffSharp.Encoding;
using Xunit;
using static VCDiffSharp.Tests.WindowVectors;

namespace VCDiffSharp.Tests;

public class DefaultEncoderTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void DefaultOverloadsEncodeOverlappingTargetCopies(int period)
    {
        byte[] target = new byte[65536];
        for (int i = 0; i < target.Length; i++) target[i] = (byte)('A' + i % period);
        byte[] expected = Patch(Window(target.Length, target[..period],
            [(byte)(period + 1), 19, .. Integer(target.Length - period)], [0]));
        Assert.Equal(expected, Vcdiff.Encode([], target));
        Assert.Equal(expected, Vcdiff.Encode([], target, VcdiffEncodeMode.Default));
        AssertDecodes([], expected, target);
        foreach (var memory in new[] { MemoryMode.Default, MemoryMode.LessMemory })
        {
            Assert.Equal(expected, Vcdiff.Encode([], target, memory));
            var sink = new ArrayBufferWriter<byte>();
            Vcdiff.Encode([], target, sink, memory);
            Assert.Equal(expected, sink.WrittenSpan.ToArray());
            sink.ResetWrittenCount();
            Vcdiff.Encode([], target, sink, VcdiffEncodeMode.Default, memory);
            Assert.Equal(expected, sink.WrittenSpan.ToArray());
        }
        Assert.True(Vcdiff.Encode([], target, VcdiffEncodeMode.Fast).Length > target.Length);
    }

    [Theory]
    [InlineData(24, 100)]
    [InlineData(400, 400)]
    public void FullSourceSearchFindsLaterLongerMatch(int length, int duplicates)
    {
        byte[] target = new byte[length];
        new Random(18).NextBytes(target);
        byte[] source = new byte[duplicates * 8 + length];
        for (int i = 0; i < duplicates; i++) target.AsSpan(0, 8).CopyTo(source.AsSpan(8 * i));
        target.CopyTo(source, duplicates * 8);
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Exhaustive);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.True(window.Data.IsEmpty);
        Assert.True(window.Addresses.Length <= 2);
        Assert.Equal(2 + (length > 127 ? 1 : 0), window.Instructions.Length);
        AssertDecodes(source, patch, target);
    }

    [Fact]
    public void TinyTargetCanUseADistantMatchInALargeSource()
    {
        byte[] source = new byte[1 << 20];
        new Random(61).NextBytes(source);
        byte[] target = source[800_003..800_027];
        byte[] patch = Vcdiff.Encode(source, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.True(window.Data.IsEmpty);
        Assert.True(patch.Length < Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast).Length);
        AssertDecodes(source, patch, target);
    }

    [Fact]
    public void EarlierTargetCandidateBeatsRecentShorterOccurrence()
    {
        byte[] word = "ABCDthe first occurrence has a long suffix"u8.ToArray();
        byte[] target = [.. word, .. "!ABCD?ABCD#"u8, .. word];
        byte[] patch = Vcdiff.Encode([], target, VcdiffEncodeMode.Exhaustive);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(0, 0, target.Length, out var window));
        Assert.Equal(0, window.Addresses[^1]);
        Assert.Equal(word.Length, window.Instructions[^1]);
        AssertDecodes([], patch, target);
    }

    [Theory]
    [InlineData(MemoryMode.Default)]
    [InlineData(MemoryMode.LessMemory)]
    public void TargetSearchFindsAnInteriorCandidateBeyond256Collisions(MemoryMode memory)
    {
        const int probe = 400 * 16;
        byte[] target = new byte[probe + 32];
        new Random(138).NextBytes(target);
        for (int i = 0; i < probe; i += 16) "seed"u8.CopyTo(target.AsSpan(i));
        target.AsSpan(80, 32).CopyTo(target.AsSpan(probe));
        var writer = new WindowWriter(0, 0, stackalloc byte[32], stackalloc byte[32], stackalloc byte[32], []);
        scoped TargetMatchFinder history = new(target, [], memory);
        try
        {
            writer.Add(target.AsSpan(0, probe));
            WindowChoice best = default;
            history.Search(probe, 0, in writer, ref best, writer.CopySavingsUpperBound(target.Length, probe));
            Assert.Equal(80, best.Address);
            Assert.Equal(32, best.Length);
        }
        finally
        {
            history.Dispose();
            writer.Dispose();
        }
    }

    [Fact]
    public void CheaperCachedAddressCanBeatASlightlyLongerCopy()
    {
        byte[] window = new byte[80];
        new Random(65).NextBytes(window);
        byte[] source = new byte[100000];
        window.AsSpan(8, 13).CopyTo(source.AsSpan(40000));
        window.AsSpan(8, 12).CopyTo(source.AsSpan(80000));
        using var finder = new SourceMatchFinder(source, window.Length, MemoryMode.Default, [], VcdiffEncodeMode.Default);
        var writer = new WindowWriter(0, source.Length, stackalloc byte[32], stackalloc byte[32],
            stackalloc byte[32], stackalloc int[768]);
        try
        {
            writer.Copy(80000, 8);
            WindowChoice best = default;
            finder.Search(window, 8, 8, in writer, ref best, int.MaxValue);
            Assert.Equal(80000, best.Address);
            Assert.Equal(12, best.Length);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void ShortTargetCopyDoesNotHideALongerUnalignedSourceMatch()
    {
        byte[] source = new byte[512];
        new Random(91).NextBytes(source);
        byte[] target = [.. source[7..16], 255, .. source[7..200]];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Exhaustive);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(Integer(193), window.Instructions[^2..].ToArray());
        AssertDecodes(source, patch, target);
    }

    [Fact]
    public void BothOutputPathsReconstructMixedSourceAndTargetData()
    {
        var random = new Random(317);
        for (int trial = 0; trial < 32; trial++)
        {
            byte[] source = new byte[random.Next(512, 8192)];
            random.NextBytes(source);
            var data = new List<byte>();
            for (int i = 0; i < 100; i++)
            {
                data.AddRange(source.AsSpan(random.Next(source.Length - 64), random.Next(1, 64)).ToArray());
                int position = random.Next(data.Count);
                data.AddRange(data.GetRange(position, Math.Min(data.Count - position, random.Next(1, 96))));
                data.Add((byte)random.Next(256));
            }
            byte[] target = data.ToArray();
            foreach (var memory in new[] { MemoryMode.Default, MemoryMode.LessMemory })
            {
                byte[] patch = Vcdiff.Encode(source, target, memory);
                var sink = new ArrayBufferWriter<byte>();
                sink.Write("prefix"u8);
                Vcdiff.Encode(source, target, sink, memory);
                Assert.Equal("prefix"u8.ToArray(), sink.WrittenSpan[..6].ToArray());
                Assert.Equal(patch, sink.WrittenSpan[6..].ToArray());
                AssertDecodes(source, patch, target);
            }
        }
    }

    [Fact]
    public void IndexesPositionsPassedBySourceCopyAndRun()
    {
        byte[] source = "an entirely original block copied from the external dictionary"u8.ToArray();
        byte[] target = [.. source, .. "----"u8, .. source[^8..], .. "----"u8];
        byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Exhaustive);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(new byte[] { (byte)'-' }, window.Data.ToArray());
        Assert.Equal(2, window.Addresses.Length);
        AssertDecodes(source, patch, target);
    }

    [Fact]
    public void EachWindowRestartsItsTargetHistoryAndAddressCache()
    {
        const int size = 8 << 20;
        byte[] target = new byte[size + 512];
        for (int i = 0; i < target.Length; i++) target[i] = (byte)(i % 4);
        byte[] patch = Vcdiff.Encode([], target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        foreach (int length in new[] { size, 512 })
        {
            Assert.True(reader.TryReadWindow(0, 0, length, out var window));
            Assert.Equal(length, window.TargetLength);
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, window.Data.ToArray());
            Assert.Equal(new byte[] { 0 }, window.Addresses.ToArray());
        }
        Assert.Equal(target, Vcdiff.Decode([], patch));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(128)]
    public void SmallWriterCallsAllocateNoManagedBytes(int length)
    {
        byte[] target = new byte[length];
        for (int i = 0; i < length; i++) target[i] = (byte)(i % 5);
        var sink = new ArrayBufferWriter<byte>(256);
        Vcdiff.Encode([], target, sink);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            sink.ResetWrittenCount();
            Vcdiff.Encode([], target, sink);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, bytes);
        Assert.Equal(target, Vcdiff.Decode([], sink.WrittenSpan));
    }
}
