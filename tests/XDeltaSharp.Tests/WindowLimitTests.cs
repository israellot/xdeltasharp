using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class WindowLimitTests
{
    private const int MiB = 1024 * 1024;
    private static byte[] Run(int size) => Window(size, [42], [0, .. Integer(size)], []);

    [Fact]
    public void AllocatingDecoderDistinguishesPositionalOutputLimitFromNamedWindowLimit()
    {
        byte[] patch = Patch(Run(8), Run(8));
        byte[] expected = Enumerable.Repeat((byte)42, 16).ToArray();

        Assert.Equal(expected, Vcdiff.Decode([], patch));
        Assert.Equal(expected, Vcdiff.Decode([], patch, maxWindowBytes: 8));
        Assert.Equal(expected, Vcdiff.Decode([], patch, maxWindowBytes: 8, maxOutputBytes: 16));
        Assert.Equal(expected, Vcdiff.Decode([], patch, 16, 8));
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, 8));
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, maxOutputBytes: 8));
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, maxWindowBytes: 7));
        Assert.Throws<ArgumentOutOfRangeException>("maxWindowBytes", () => Vcdiff.Decode([], patch, maxWindowBytes: -1));
    }

    [Fact]
    public void AcceptsExactly64MiB()
    {
        Assert.Equal(64 * MiB, Vcdiff.MaxTargetWindowBytes);
        byte[] patch = Patch(Run(64 * MiB));
        byte[] result = Vcdiff.Decode([], patch);
        Assert.Equal(64 * MiB, result.Length);
        Assert.Equal(-1, result.AsSpan().IndexOfAnyExcept((byte)42));
        Assert.Equal(result.Length, Vcdiff.Decode([], patch, result.AsSpan()));
        using var sink = new CheckingSink();
        Assert.Equal(result.LongLength, Vcdiff.Decode(Stream.Null, new MemoryStream(patch), sink));
    }

    [Theory]
    [InlineData(64L * MiB + 1)]
    [InlineData(1L << 31)]
    public void RejectsOversizedWindowAsSoonAsItsLengthIsRead(long size)
    {
        // End immediately after the target length to verify rejection precedes section reads.
        byte[] patch = Patch([0, 24, .. Integer(size)]);
        Assert.Contains("window limit", Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch)).Message);
        Assert.Contains("window limit", Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, Span<byte>.Empty)).Message);
        Assert.Contains("window limit", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new MemoryStream(patch), Stream.Null)).Message);
    }

    [Fact]
    public void WindowLimitMayBeTightenedIndependentlyOfTotalOutput()
    {
        byte[] patch = Patch(Run(8), Run(8));
        Assert.Equal(16, Vcdiff.Decode([], patch, maxOutputBytes: 16, maxWindowBytes: 8).Length);
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, 16, 7));
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, new byte[16], 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Decode([], patch, 16, 64 * MiB + 1));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new MemoryStream(patch), output, maxWindowBytes: 7));
        Assert.Equal(0, output.Length);
        Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new MemoryStream(patch), output, maxOutputBytes: 15, maxWindowBytes: 8));
        Assert.Equal(8, output.Length);
    }

    [Fact]
    public void StreamsMoreThanTwoGiBInEightMiBWindows()
    {
        const int size = 8 * MiB;
        byte[] patch = Patch(Enumerable.Repeat(Run(size), 257).ToArray());
        using var sink = new CheckingSink();
        long written = Vcdiff.Decode(Stream.Null, new MemoryStream(patch), sink);
        Assert.Equal(257L * size, written);
        Assert.Equal(written, sink.VerifiedBytes);
    }

    [Fact]
    public void ReadsSourceSegmentsAndCopyAddressesBeyondFourGiB()
    {
        const long offset = (1L << 32) + 5;
        using var source = new OffsetSource("abcd"u8.ToArray(), 3 + offset);
        source.Position = 3;
        byte[] patch = Patch(Window(4, [], [20], [0], 1, 4, offset),
            Window(8, [], [20, 52], [.. Integer(offset), 0], 1, offset + 4));
        using var output = new MemoryStream();
        Assert.Equal(12, Vcdiff.Decode(source, new MemoryStream(patch), output));
        Assert.Equal("abcdabcdabcd"u8.ToArray(), output.ToArray());
        Assert.Equal(3, source.Position);
    }

    [Fact]
    public void PriorTargetUsesOriginalOutputPositionAndRestoresWriteCursor()
    {
        byte[] patch = Patch(Window(6, "ABCDEF"u8.ToArray(), [7], []), Window(4, [], [20], [0], 2, 4, 1));
        using var output = new MemoryStream();
        output.Write("prefix"u8);
        using var input = new ShortReadPatch(patch);
        Assert.Equal(10, Vcdiff.Decode(Stream.Null, input, output));
        Assert.Equal("prefixABCDEFBCDE"u8.ToArray(), output.ToArray());
        Assert.Equal(output.Length, output.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidStreamArgumentsIdentifyTheParameterBeforeConsumingInput(bool useLzma)
    {
        long Decode(Stream source, Stream patch, Stream destination) => useLzma
            ? LzmaVcdiff.Decode(source, patch, destination)
            : Vcdiff.Decode(source, patch, destination);

        using var patch = new MemoryStream(Patch(), writable: false);
        using var source = new MemoryStream();
        using var destination = new MemoryStream();
        using var writeOnly = new CheckingSink();

        Assert.Throws<ArgumentNullException>("source", () => Decode(null!, patch, destination));
        Assert.Throws<ArgumentNullException>("patch", () => Decode(source, null!, destination));
        Assert.Throws<ArgumentNullException>("destination", () => Decode(source, patch, null!));
        Assert.Throws<ArgumentException>("patch", () => Decode(source, writeOnly, destination));
        Assert.Throws<ArgumentException>("destination", () => Decode(source, destination, patch));
        Assert.Throws<ArgumentException>("patch", () => Decode(patch, patch, destination));
        Assert.Throws<ArgumentException>("destination", () => Decode(source, destination, destination));
        Assert.Throws<ArgumentException>("destination", () => Decode(destination, patch, destination));
        Assert.Equal(0, patch.Position);
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPriorTargetWindowRestoresCursorsAndLeavesStreamsOpen(bool useLzma)
    {
        using var source = new MemoryStream("-abcd"u8.ToArray()) { Position = 1 };
        using var patch = new MemoryStream(Patch(Window(4, [], [20], [0], 1, 4),
            Window(4, [], [20], [0], 2, 4, checksum: 0)));
        using var output = new MemoryStream();
        output.WriteByte((byte)'+');

        Assert.Throws<InvalidDataException>(() => useLzma
            ? LzmaVcdiff.Decode(source, patch, output)
            : Vcdiff.Decode(source, patch, output));

        Assert.Equal(1, source.Position);
        Assert.True(patch.CanRead);
        Assert.Equal(5, output.Position);
        output.WriteByte((byte)'!');
        Assert.Equal("+abcd!"u8.ToArray(), output.ToArray());
    }

    [Fact]
    public void WriteOnlyDestinationCanDecodeUntilAPriorTargetDictionaryIsNeeded()
    {
        byte[] patch = Patch(Run(8), Window(4, [], [20], [0], 2, 4));
        using var input = new MemoryStream(patch);
        using var sink = new CheckingSink();
        Assert.Throws<NotSupportedException>(() => Vcdiff.Decode(Stream.Null, input, sink));
        Assert.Equal(8, sink.VerifiedBytes);
        Assert.True(input.Position < input.Length);
    }

    [Fact]
    public void SkipsOpaqueApplicationHeaderOnSequentialShortReads()
    {
        byte[] metadata = new byte[4097];
        new Random(7).NextBytes(metadata);
        byte[] patch = [0xD6, 0xC3, 0xC4, 0, 4, .. Integer(metadata.Length), .. metadata, .. Run(8)];
        using var input = new ShortReadPatch(patch);
        using var sink = new CheckingSink();
        Assert.Equal(8, Vcdiff.Decode(Stream.Null, input, sink));
        Assert.Equal(8, sink.VerifiedBytes);
    }

    private sealed class ShortReadPatch(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(2, buffer.Length)]);
    }

    // Model a large dictionary without reserving gigabytes of disk space in the test suite.
    private sealed class OffsetSource(byte[] tail, long tailStart) : MemoryStream
    {
        public override long Length => tailStart + tail.Length;
        public override long Position { get; set; }
        public override int Read(Span<byte> buffer)
        {
            int offset = checked((int)(Position - tailStart));
            int count = Math.Min(buffer.Length, tail.Length - offset);
            tail.AsSpan(offset, count).CopyTo(buffer);
            Position += count;
            return count;
        }
    }

    private sealed class CheckingSink : Stream
    {
        public long VerifiedBytes { get; private set; }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Assert.Equal(-1, buffer.IndexOfAnyExcept((byte)42));
            VerifiedBytes += buffer.Length;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
