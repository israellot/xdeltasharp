using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class EncodedWindowLimitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChecksTheCombinedSectionSizesPerWindowBeforeReadingPayload(bool lzma)
    {
        // One data byte, two instruction bytes and one address byte. The checksum is part of the header.
        byte[] window = Window(5, [(byte)'!'], [2, 20], [0], 1, 4, checksum: 0x047E01AC);
        byte[] largerPayload = Window(4, "last"u8.ToArray(), [5], []);
        using var source = new MemoryStream("_abcd"u8.ToArray()) { Position = 1 };
        using var input = new MemoryStream(Patch(window, window, largerPayload));
        using var output = new MemoryStream();

        Assert.Contains("window payload limit", Assert.Throws<InvalidDataException>(() =>
            Decode(lzma, source, input, output, maxEncodedWindowBytes: 4)).Message);
        Assert.Equal("!abcd!abcd"u8.ToArray(), output.ToArray());
        Assert.Equal(input.Length - 5, input.Position);
        Assert.Equal(1, source.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroBudgetAcceptsEmptyWindows(bool lzma)
    {
        using var input = new MemoryStream(Patch(Window(0, [], [], [], checksum: 1), Window(0, [], [], [])));
        Assert.Equal(0, Decode(lzma, Stream.Null, input, Stream.Null, maxEncodedWindowBytes: 0));
        Assert.Equal(input.Length, input.Position);
    }

    [Fact]
    public void RejectsABudgetOutsideTheArrayRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VcdiffDecodeOptions { MaxEncodedWindowBytes = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VcdiffDecodeOptions { MaxEncodedWindowBytes = Array.MaxLength + 1 });
        Assert.Equal(Array.MaxLength, new VcdiffDecodeOptions().MaxEncodedWindowBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputAndTargetWindowLimitsRemainIndependent(bool lzma)
    {
        byte[] run = Window(32, [42], [0, 32], []);
        using var input = new MemoryStream(Patch(run, run));
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() => Decode(lzma, Stream.Null, input, output,
            maxEncodedWindowBytes: 3, maxOutputBytes: 64, maxWindowBytes: 31));
        Assert.Equal(0, output.Length);
        input.Position = 0;
        Assert.Throws<InvalidDataException>(() => Decode(lzma, Stream.Null, input, output,
            maxEncodedWindowBytes: 3, maxOutputBytes: 63, maxWindowBytes: 32));
        Assert.Equal(32, output.Length);
    }

    [Fact]
    public void AppliesTheBudgetToCompressedBytesAcrossAnArmoredLzmaPatch()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var source = File.OpenRead(Path.Combine(fixtures, "source.txt"));
        using var patch = File.OpenRead(Path.Combine(fixtures, "xdelta3-lzma.vcdiff"));
        using var output = new MemoryStream();

        // This patch is less than 1 KiB on the wire and reconstructs three windows over 16 MiB.
        long written = LzmaVcdiff.Decode(source, patch, output,
            new LzmaDecodeOptions { MaxEncodedWindowBytes = 1024, Armor = Blake3Armor.Verify });
        Assert.Equal(16_779_027, written);
        Assert.Equal(written, output.Length);
    }

    private static long Decode(bool lzma, Stream source, Stream patch, Stream output, int maxEncodedWindowBytes,
        long maxOutputBytes = long.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => lzma
            ? LzmaVcdiff.Decode(source, patch, output, new LzmaDecodeOptions
            {
                MaxEncodedWindowBytes = maxEncodedWindowBytes,
                MaxOutputBytes = maxOutputBytes,
                MaxWindowBytes = maxWindowBytes
            })
            : Vcdiff.Decode(source, patch, output, new VcdiffDecodeOptions
            {
                MaxEncodedWindowBytes = maxEncodedWindowBytes,
                MaxOutputBytes = maxOutputBytes,
                MaxWindowBytes = maxWindowBytes
            });
}
