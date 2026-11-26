using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class LzmaDecoderTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

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

        byte[] result = verifyArmor
            ? LzmaVcdiff.Decode(source, patch, maxOutputBytes: expected.Length, armor: Blake3Armor.Verify)
            : LzmaVcdiff.Decode(source, patch, maxOutputBytes: expected.Length);
        Assert.True(expected.AsSpan().SequenceEqual(result));
        Array.Clear(result);
        Assert.Equal(expected.Length, verifyArmor
            ? LzmaVcdiff.Decode(source, patch, result.AsSpan(), armor: Blake3Armor.Verify)
            : LzmaVcdiff.Decode(source, patch, result.AsSpan()));
        Assert.True(expected.AsSpan().SequenceEqual(result));
        using var output = new MemoryStream(expected.Length);
        using var sourceStream = new MemoryStream(source);
        using var patchStream = new MemoryStream(patch);
        Assert.Equal(expected.LongLength, verifyArmor
            ? LzmaVcdiff.Decode(sourceStream, patchStream, output, armor: Blake3Armor.Verify)
            : LzmaVcdiff.Decode(sourceStream, patchStream, output));
        Assert.True(expected.AsSpan().SequenceEqual(output.GetBuffer().AsSpan(0, (int)output.Length)));
    }

    [Theory]
    [InlineData("xdelta3.vcdiff")]
    [InlineData("xdelta3-checksum.vcdiff")]
    public void OptionalPackageAlsoDecodesPlainSections(string fixture)
    {
        Assert.Equal(Fixture("target.txt"), LzmaVcdiff.Decode(Fixture("source.txt"), Fixture(fixture)));
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
        byte[] header = Convert.FromHexString("FD377A585A000000FF12D941020021010C0000008F98419C");
        byte[] data = [.. Convert.FromHexString(size), .. header, .. Convert.FromHexString(chunk)];
        byte[] delta = [3, 1, (byte)data.Length, 1, 0, .. data, 4];
        byte[] patch = [0xD6, 0xC3, 0xC4, 0, 1, 2, 0, (byte)delta.Length, .. delta];
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode([], patch));
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode([], patch, new byte[3]));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => LzmaVcdiff.Decode(Stream.Null, new MemoryStream(patch), output));
        Assert.Equal(0, output.Length);
    }
}
