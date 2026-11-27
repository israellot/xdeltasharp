using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class LzmaDecoderTests
{
    private static LzmaDecodeOptions Limits(int sectionBytes, int dictionaryBytes)
        => new() { MaxExpandedSectionBytes = sectionBytes, MaxDictionaryBytes = dictionaryBytes };

    // xdelta3's XZ stream and block headers for a 256 KiB LZMA2 dictionary.
    private static readonly byte[] XzHeader = Convert.FromHexString("FD377A585A000000FF12D941020021010C0000008F98419C");

    // One uncompressed LZMA2 chunk after the header.
    private static byte[] StoredSection(byte[] bytes) =>
        [.. Integer(bytes.Length), .. XzHeader, 1, 0, (byte)(bytes.Length - 1), .. bytes];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodesDefaultXdelta3AcrossMixedSectionWindows(bool verifyArmor)
    {
        byte[] source = Fixture("source.txt");
        byte[] sample = Fixture("target.txt");
        byte[] patch = Fixture("xdelta3-lzma.vcdiff");
        // Recreate multi.bin independently of the patch. See Fixtures/README.md.
        using var prefix = new MemoryStream();
        prefix.Write(sample);
        for (int i = 0; i < 48; i++)
        {
            prefix.Write(source.AsSpan(i * 7 % 110, 32));
            prefix.WriteByte((byte)(65 + i));
        }
        const int windowSize = 8 * 1024 * 1024;
        byte[] head = prefix.ToArray();
        byte[] expected = new byte[2 * windowSize + head.Length + 1];
        head.CopyTo(expected, 0);
        sample.CopyTo(expected, windowSize);
        expected.AsSpan(windowSize + sample.Length, windowSize - sample.Length).Fill(0xff);
        head.CopyTo(expected, 2 * windowSize);
        expected[^1] = (byte)'!';

        var options = new LzmaDecodeOptions
        {
            MaxOutputBytes = expected.Length,
            Armor = verifyArmor ? Blake3Armor.Verify : default
        };
        byte[] result = LzmaVcdiff.Decode(source, patch, options);
        Assert.True(expected.AsSpan().SequenceEqual(result));
        Array.Clear(result);
        Assert.Equal(expected.Length, LzmaVcdiff.Decode(source, patch, result.AsSpan(), options));
        Assert.True(expected.AsSpan().SequenceEqual(result));
        using var output = new MemoryStream(expected.Length);
        using var sourceStream = new MemoryStream(source);
        using var patchStream = new MemoryStream(patch);
        Assert.Equal(expected.LongLength, LzmaVcdiff.Decode(sourceStream, patchStream, output, options));
        Assert.True(expected.AsSpan().SequenceEqual(output.GetBuffer().AsSpan(0, (int)output.Length)));
    }

    [Theory]
    [InlineData("xdelta3.vcdiff")]
    [InlineData("xdelta3-checksum.vcdiff")]
    public void AlsoDecodesUncompressedPatches(string fixture)
    {
        Assert.Equal(Fixture("target.txt"), LzmaVcdiff.Decode(Fixture("source.txt"), Fixture(fixture)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void AppliesInclusiveSecondaryLimitsToEachSection(byte compressedSection)
    {
        byte[] source = [(byte)'q'];
        byte[] data = "abc"u8.ToArray();
        byte[] instructions = [4, 19, 1]; // ADD 3, COPY 1
        byte[] addresses = [0x80, 0x80, 0]; // SELF address 0
        byte[] window = Window(4, compressedSection == 1 ? StoredSection(data) : data,
            compressedSection == 2 ? StoredSection(instructions) : instructions,
            compressedSection == 4 ? StoredSection(addresses) : addresses, indicator: 1, segmentLength: 1,
            compressedSections: compressedSection);
        byte[] patch = LzmaPatch(window);
        const int dictionarySize = 256 * 1024;
        byte[] expected = "abcq"u8.ToArray();
        var exact = Limits(sectionBytes: 3, dictionarySize);
        Assert.Equal(expected, LzmaVcdiff.Decode(source, patch, exact));
        var destination = new byte[4];
        Assert.Equal(4, LzmaVcdiff.Decode(source, patch, destination, exact));
        Assert.Equal(expected, destination);
        using var output = new MemoryStream();
        Assert.Equal(4, LzmaVcdiff.Decode(new MemoryStream(source), new MemoryStream(patch), output, exact));
        Assert.Equal(expected, output.ToArray());

        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode(source, patch, Limits(2, dictionarySize)));
        Assert.Throws<InvalidDataException>(() =>
            LzmaVcdiff.Decode(source, patch, destination, Limits(3, dictionarySize - 1)));
        using var rejected = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode(new MemoryStream(source),
            new MemoryStream(patch), rejected, Limits(0, dictionarySize)));
        Assert.Equal(0, rejected.Length);
    }

    [Fact]
    public void EmptyRunCanConsumeADataByteBeyondTheTargetLength()
    {
        byte[] window = Window(1, StoredSection("xy"u8.ToArray()), [0, 0, 2], [], compressedSections: 1);
        byte[] patch = LzmaPatch(window);
        Assert.Equal("y"u8.ToArray(),
            LzmaVcdiff.Decode([], patch, new LzmaDecodeOptions { MaxOutputBytes = 1, MaxWindowBytes = 1 }));
    }

    [Fact]
    public void ZeroSecondaryLimitsAcceptRawSections()
    {
        byte[] window = Window(3, "abc"u8.ToArray(), [4], []);
        byte[] patch = LzmaPatch(window);
        Assert.Equal("abc"u8.ToArray(), LzmaVcdiff.Decode([], patch, Limits(0, 0)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(Vcdiff.MaxTargetWindowBytes + 1, 0)]
    [InlineData(0, Vcdiff.MaxTargetWindowBytes + 1)]
    public void RejectsSecondaryLimitsOutsideTheWindowRange(int maxExpandedSectionBytes, int maxDictionaryBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Limits(maxExpandedSectionBytes, maxDictionaryBytes));
    }

    [Theory]
    [Trait("Category", "Allocation")]
    [InlineData(false)]
    [InlineData(true)]
    public void RawSectionsDecodeIntoASpanWithoutAllocating(bool hasLzmaHeader)
    {
        byte[] window = Window(3, "abc"u8.ToArray(), [4], []);
        byte[] patch = hasLzmaHeader ? LzmaPatch(window) : Patch(window);

        var target = new byte[3];
        var limited = Limits(128, 4096);
        LzmaVcdiff.Decode([], patch, target);
        LzmaVcdiff.Decode([], patch, target, limited);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            LzmaVcdiff.Decode([], patch, target);
            LzmaVcdiff.Decode([], patch, target, limited);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(0, allocated);
        Assert.Equal("abc"u8.ToArray(), target);
    }

    [Theory]
    [InlineData("00", "")]                 // Empty expanded section
    [InlineData("A0808001", "")]           // Expanded section above 64 MiB
    [InlineData("03", "0100026162")]       // Truncated chunk
    [InlineData("03", "010002616263FF")]   // Unconsumed section byte
    [InlineData("02", "010002616263")]     // More chunk output than declared
    [InlineData("04", "010002616263")]     // Less chunk output than declared
    public void RejectsInvalidCompressedSections(string size, string chunk)
    {
        byte[] data = [.. Convert.FromHexString(size), .. XzHeader, .. Convert.FromHexString(chunk)];
        byte[] delta = [3, 1, (byte)data.Length, 1, 0, .. data, 4];
        byte[] patch = LzmaPatch([0, (byte)delta.Length, .. delta]);
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode([], patch));
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode([], patch, new byte[3]));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode(Stream.Null, new MemoryStream(patch), output));
        Assert.Equal(0, output.Length);
    }
}
