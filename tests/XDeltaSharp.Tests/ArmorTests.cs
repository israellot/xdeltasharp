using XDeltaSharp.Blake3;
using Xunit;

namespace XDeltaSharp.Tests;

public class ArmorTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData("xdelta3-checksum.vcdiff")]
    [InlineData("xdelta3.vcdiff")]
    public void VerifiesArmoredAndAcceptsLegacyPatch(string name)
    {
        byte[] source = Fixture("source.txt"), target = Fixture("target.txt"), patch = Fixture(name);
        Assert.Equal(target, Vcdiff.Decode(source, patch, armor: Blake3Armor.Verify));
        byte[] buffer = new byte[target.Length + 8];
        Assert.Equal(target.Length, Vcdiff.Decode(source, patch, buffer.AsSpan(3), armor: Blake3Armor.Verify));
        Assert.True(target.AsSpan().SequenceEqual(buffer.AsSpan(3, target.Length)));

        using var input = new MemoryStream([42, .. source]) { Position = 1 };
        using var output = new MemoryStream();
        output.WriteByte(123);
        Assert.Equal(target.Length, Vcdiff.Decode(input, new ShortReadPatch(patch), output, armor: Blake3Armor.Verify));
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
            Vcdiff.Decode(source, patch, armor: Blake3Armor.Verify)).Message);
        byte[] destination = new byte[target.Length];
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, destination, armor: Blake3Armor.Verify)).Message);
        Assert.Equal(wrongSource ? new byte[target.Length] : target, destination);

        using var input = new MemoryStream([0, .. source]) { Position = 1 };
        using var output = new MemoryStream();
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(input, new ShortReadPatch(patch), output, armor: Blake3Armor.Verify)).Message);
        Assert.Equal(1, input.Position);
        Assert.Equal(wrongSource ? [] : target, output.ToArray());
    }

    [Fact]
    public void TargetOnlyArmorUsesLastHashInFilenameAndHashesEmptyOutput()
    {
        const string emptyHash = "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262";
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"file#name#{emptyHash}/");
        byte[] patch = [0xD6, 0xC3, 0xC4, 0, 4, (byte)header.Length, .. header];
        Assert.Empty(Vcdiff.Decode([], patch, armor: Blake3Armor.Verify));
        patch[^2] = (byte)'0';
        Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, new ShortReadPatch(patch), Stream.Null, armor: Blake3Armor.Verify));
    }

    [Fact]
    public void CannotVerifyAnUnseekableArmoredSource()
    {
        using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => Vcdiff.Decode(
            new ShortReadPatch(Fixture("source.txt")), new ShortReadPatch(Fixture("xdelta3-checksum.vcdiff")), output, armor: Blake3Armor.Verify));
        Assert.Equal(0, output.Length);
    }

    private sealed class ShortReadPatch(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(3, buffer.Length)]);
    }
}
