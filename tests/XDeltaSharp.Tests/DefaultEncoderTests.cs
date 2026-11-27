using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

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
        AssertDecodes([], expected, target);
        foreach (var memory in new[] { MemoryMode.Default, MemoryMode.LessMemory })
        {
            var options = new VcdiffEncodeOptions { MemoryMode = memory };
            Assert.Equal(expected, Vcdiff.Encode([], target, options));
            var sink = new ArrayBufferWriter<byte>();
            Vcdiff.Encode([], target, sink, options);
            Assert.Equal(expected, sink.WrittenSpan.ToArray());
        }
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
        AssertDecodes(source, patch, target);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(256)]
    public void ShortTargetBoundsDistantSearchAndStillFindsAMovedSuffix(int length)
    {
        byte[] source = Bytes(1 << 20);
        const int offset = 800_003;
        byte[] target = source[offset..(offset + length)];
        using var matcher = new LevelSourceMatcher(source, length, 8, MemoryMode.Default, []);
        int distantHits = 0;
        // Every probe has a distant match. With prediction disabled, each hit requires a scan.
        for (int probe = 0; probe <= length - 4; probe++)
        {
            if (matcher.Find(target, probe, 0, 4, -1).Length != 0) distantHits++;
            Assert.Equal(length, matcher.Find(target, probe, 0, 4, offset + probe).Length);
        }
        Assert.InRange(distantHits, 1, 8);

        target[0] ^= 0xFF;
        byte[] patch = Vcdiff.Encode(source, target);
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        Assert.Equal(target[..1], window.Data.ToArray());
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
                var options = new VcdiffEncodeOptions { MemoryMode = memory };
                byte[] patch = Vcdiff.Encode(source, target, options);
                var sink = new ArrayBufferWriter<byte>();
                sink.Write("prefix"u8);
                Vcdiff.Encode(source, target, sink, options);
                Assert.Equal("prefix"u8.ToArray(), sink.WrittenSpan[..6].ToArray());
                Assert.Equal(patch, sink.WrittenSpan[6..].ToArray());
                AssertDecodes(source, patch, target);
            }
        }
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
    [Trait("Category", "Allocation")]
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

    [Fact]
    public void EmitsBaselineWindowsForEmptyLiteralRunAndSourceCopy()
    {
        AssertPatch([], [], Patch(Window(0, [], [], [])));
        AssertPatch([], "abc"u8.ToArray(), Patch(Window(3, "abc"u8.ToArray(), [4], [])));
        AssertPatch([], "xxx"u8.ToArray(), Patch(Window(3, "xxx"u8.ToArray(), [4], [])));
        AssertPatch([], new byte[256], Patch(Window(256, [0], [0, .. Integer(256)], [])));
        byte[] source = "0123456789abcdef"u8.ToArray();
        AssertPatch(source, source, Patch(Window(16, [], [32], [0], 1, 16)));
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
    [InlineData(2048)]
    [InlineData(2049)]
    [InlineData(524288)]
    [InlineData(524289)]
    public void CompactTableBoundariesAndDeterministicSinkOutput(int length)
    {
        byte[] source = Bytes(length);
        byte[] target = [.. source.AsSpan(length / 2), 5, 6, 7, .. source.AsSpan(0, length / 2)];
        AssertPatch(source, target, Vcdiff.Encode(source, target));
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

    [Theory]
    [Trait("Category", "Allocation")]
    [InlineData(16, MemoryMode.Default)]
    [InlineData(64, MemoryMode.Default)]
    [InlineData(16, MemoryMode.LessMemory)]
    [InlineData(64, MemoryMode.LessMemory)]
    public void SmallCopyEncodingOnlyAllocatesTheReturnedArray(int copyLength, MemoryMode memoryMode)
    {
        byte[] source = Bytes(128);
        byte[] target = [21, .. source.AsSpan(8, copyLength), 42];
        var sink = new ArrayBufferWriter<byte>(128);
        // Build the options once. They are the caller's allocation, not the encoder's.
        var options = new VcdiffEncodeOptions { MemoryMode = memoryMode };
        byte[] expected = Vcdiff.Encode(source, target, options);
        Vcdiff.Encode(source, target, sink, options);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            sink.ResetWrittenCount();
            Vcdiff.Encode(source, target, sink, options);
        }
        long writerBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) GC.KeepAlive(Vcdiff.Encode(source, target, options));
        long allocatingBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) GC.KeepAlive(new byte[expected.Length]);
        long outputArrayBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, writerBytes);
        Assert.Equal(outputArrayBytes, allocatingBytes);
        Assert.Equal(1, expected[5]); // A source COPY was actually used.
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(17).NextBytes(bytes);
        return bytes;
    }

    private static void AssertPatch(byte[] source, byte[] target, byte[] expected)
    {
        Assert.Equal(expected, Vcdiff.Encode(source, target));
        var sink = new ArrayBufferWriter<byte>();
        sink.Write("prefix"u8);
        Vcdiff.Encode(source, target, sink);
        Assert.Equal("prefix"u8.ToArray(), sink.WrittenSpan[..6].ToArray());
        Assert.Equal(expected, sink.WrittenSpan[6..].ToArray());
        AssertDecodes(source, expected, target);
    }
}
