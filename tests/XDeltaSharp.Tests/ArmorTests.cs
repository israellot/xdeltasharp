using XDeltaSharp.Blake3;
using Xunit;

namespace XDeltaSharp.Tests;

public class ArmorTests
{
    private const string EmptyHash = "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262";

    private static readonly VcdiffDecodeOptions Verify = new() { Armor = Blake3Armor.Verify };
    private static readonly VcdiffDecodeOptions Require = new() { Armor = Blake3Armor.Require };

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData("xdelta3-checksum.vcdiff")]
    [InlineData("xdelta3.vcdiff")]
    public void VerifiesArmoredAndAcceptsLegacyPatch(string name)
    {
        byte[] source = Fixture("source.txt"), target = Fixture("target.txt"), patch = Fixture(name);
        Assert.Equal(target, Vcdiff.Decode(source, patch, Verify));
        byte[] buffer = new byte[target.Length + 8];
        Assert.Equal(target.Length, Vcdiff.Decode(source, patch, buffer.AsSpan(3), Verify));
        Assert.True(target.AsSpan().SequenceEqual(buffer.AsSpan(3, target.Length)));

        using var input = new MemoryStream([42, .. source]) { Position = 1 };
        using var output = new MemoryStream();
        output.WriteByte(123);
        Assert.Equal(target.Length, Vcdiff.Decode(input, new ShortReadPatch(patch), output, Verify));
        Assert.Equal(new byte[] { 123 }.Concat(target), output.ToArray());
        Assert.Equal(1, input.Position);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReportsWhichWholeFileDigestFailedInEveryOverload(bool wrongSource)
    {
        byte[] source = Fixture("source.txt"), target = Fixture("target.txt"), patch = Fixture("xdelta3-checksum.vcdiff");
        if (wrongSource)
            source = [.. source, 42]; // No COPY references this byte; the per-window checksum still passes.
        else
            patch[Array.IndexOf(patch, (byte)'#') + 1] = (byte)'0'; // Change only the first target hash digit.
        Assert.Equal(target, Vcdiff.Decode(source, patch));

        string error = wrongSource ? "BLAKE3 source" : "BLAKE3 target";
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, Verify)).Message);
        byte[] destination = new byte[target.Length];
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, destination, Verify)).Message);
        Assert.Equal(wrongSource ? new byte[target.Length] : target, destination);

        using var input = new MemoryStream([0, .. source]) { Position = 1 };
        using var output = new MemoryStream();
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(input, new ShortReadPatch(patch), output, Verify)).Message);
        Assert.Equal(1, input.Position);
        Assert.Equal(wrongSource ? [] : target, output.ToArray());
    }

    [Fact]
    public void TargetOnlyArmorUsesLastHashInFilenameAndHashesEmptyOutput()
    {
        byte[] patch = WithHeader($"file#name#{EmptyHash}/");
        Assert.Empty(Vcdiff.Decode([], patch, Verify));
        Assert.Empty(Vcdiff.Decode([], patch, Require));
        patch[^2] = (byte)'0';
        Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new ShortReadPatch(patch), Stream.Null, Verify));
    }

    [Fact]
    public void RequiredArmorVerifiesBothDigestsFromXdelta3()
    {
        byte[] source = Fixture("source.txt"), target = Fixture("target.txt"), patch = Fixture("xdelta3-checksum.vcdiff");
        Assert.Equal(target, Vcdiff.Decode(source, patch, Require));
        using var output = new MemoryStream();
        Assert.Equal(target.Length, Vcdiff.Decode(new MemoryStream(source), new ShortReadPatch(patch), output, Require));
        Assert.Equal(target, output.ToArray());

        source[0] ^= 1;
        Assert.Contains("source digest mismatch", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, Require)).Message);
        source[0] ^= 1;
        int digit = Array.IndexOf(patch, (byte)'#') + 1;
        patch[digit] = patch[digit] == '0' ? (byte)'1' : (byte)'0';
        Assert.Contains("target digest mismatch", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, Require)).Message);
    }

    [Theory]
    [InlineData(null, "target")]
    [InlineData("legacy/", "target")]
    [InlineData("file#bad/", "target")]
    [InlineData("file#" + EmptyHash + "//source", "target")]
    [InlineData("file#" + EmptyHash + "//source/", "source")]
    public void RequiredArmorRejectsMissingDigestsAcrossOutputTypes(string? metadata, string missing)
    {
        byte[] patch = metadata == null ? WindowVectors.Patch() : WithHeader(metadata);
        string expected = $"BLAKE3 {missing} digest";
        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode([], patch, Require)).Message);
        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode([], patch, Array.Empty<byte>(), Require)).Message);
        using var output = new MemoryStream();
        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new ShortReadPatch(patch), output, Require)).Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void RequiredArmorChecksForSourceDigestWhenAWindowDeclaresASource()
    {
        byte[] window = WindowVectors.Window(1, [], [19, 1], [0], indicator: 1, segmentLength: 1);
        byte[] patch = [.. WithHeader($"target#{EmptyHash}/"), .. window];
        using var output = new MemoryStream();
        Assert.Contains("BLAKE3 source digest", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(new MemoryStream([42]), new ShortReadPatch(patch), output, Require)).Message);
        Assert.Equal(0, output.Length);
    }

    private static byte[] WithHeader(string metadata)
    {
        byte[] header = System.Text.Encoding.ASCII.GetBytes(metadata);
        return [0xD6, 0xC3, 0xC4, 0, 4, .. WindowVectors.Integer(header.Length), .. header];
    }

    [Fact]
    public void CannotVerifyAnUnseekableArmoredSource()
    {
        using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => Vcdiff.Decode(
            new ShortReadPatch(Fixture("source.txt")), new ShortReadPatch(Fixture("xdelta3-checksum.vcdiff")), output, Verify));
        Assert.Equal(0, output.Length);
    }

    private sealed class ShortReadPatch(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(3, buffer.Length)]);
    }
}
