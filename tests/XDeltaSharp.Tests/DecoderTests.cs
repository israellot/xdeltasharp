using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class DecoderTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(5551)]
    [InlineData(5552)]
    [InlineData(5553)]
    public void ChecksEveryByteAcrossAdlerBlockBoundaries(int length)
    {
        byte[] bytes = new byte[length];
        new Random(37).NextBytes(bytes);
        uint a = 1, b = 0;
        foreach (byte value in bytes)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        byte[] patch = Patch(Window(length, bytes, [1, .. Integer(length)], [], checksum: (b << 16) | a));
        AssertDecodes([], patch, bytes);
    }

    [Theory]
    [InlineData("xdelta3")]
    [InlineData("xdelta3-checksum")]
    [InlineData("open-vcdiff")]
    public void DecodesIndependentEncoderFixture(string encoder)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        AssertDecodes(File.ReadAllBytes(Path.Combine(dir, "source.txt")),
            File.ReadAllBytes(Path.Combine(dir, encoder + ".vcdiff")), File.ReadAllBytes(Path.Combine(dir, "target.txt")));
    }

    [Fact]
    public void RejectsCorruptedXdelta3ChecksumInEveryOverload()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] source = File.ReadAllBytes(Path.Combine(dir, "source.txt"));
        byte[] patch = File.ReadAllBytes(Path.Combine(dir, "xdelta3-checksum.vcdiff"));
        byte[] target = File.ReadAllBytes(Path.Combine(dir, "target.txt"));

        // The four checksum bytes reported by xdelta3 printhdrs for this fixture.
        int offset = patch.AsSpan().IndexOf(Convert.FromHexString("92F15237"));
        Assert.True(offset >= 0);
        patch[offset] ^= 1;

        Assert.Contains("Adler-32", Assert.Throws<InvalidDataException>(() => Vcdiff.Decode(source, patch)).Message);
        Assert.Contains("Adler-32", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(source, patch, new byte[target.Length])).Message);
        using var output = new MemoryStream();
        Assert.Contains("Adler-32", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(new MemoryStream(source), new MemoryStream(patch), output)).Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void VerifiesEachWindowIncludingPriorTargetAndEmptyWindows()
    {
        const int run = 11105;
        byte[] patch = Patch(Window(run, [255], [0, .. Integer(run)], [], checksum: 0x37A33825),
            Window(4, [], [20], [0], 2, 4, checksum: 0x09FA03FD), Window(0, [], [], [], checksum: 1));
        AssertDecodes([], patch, Enumerable.Repeat((byte)255, run + 4).ToArray());
    }

    [Fact]
    public void EmptyFileAndEmptyWindows()
    {
        AssertDecodes([], Patch(), []);
        AssertDecodes([], Patch(Window(0, [], [], []), Window(0, [], [], [])), []);
    }

    [Fact]
    public void ReconstructsRfcSectionThreeExample()
    {
        var patch = Patch(Window(28, "wxyzz"u8.ToArray(), [20, 5, 20, 28, 0, 4], [0, 4, 24], 1, 16));
        AssertDecodes("abcdefghijklmnop"u8.ToArray(), patch, "abcdwxyzefghefghefghefghzzzz"u8.ToArray());
    }

    [Fact]
    public void ExpandsAnOverlappingCopyFromCurrentTargetWithoutADictionary()
    {
        var patch = Patch(Window(65_536, "ABCD"u8.ToArray(), [5, 19, .. Integer(65_532)], [0]));
        AssertDecodes([], patch, Enumerable.Repeat("ABCD"u8.ToArray(), 16_384).SelectMany(word => word).ToArray());
    }

    [Fact]
    public void DictionaryMaySpanMultipleCompletedTargetWindows()
    {
        byte[] patch = Patch(Window(4, [], [20], [0], 1, 4, 2), Window(4, "WXYZ"u8.ToArray(), [5], []),
            Window(12, [], [22, 22], [0, 6], 2, 6, 1));
        AssertDecodes("--abcd--"u8.ToArray(), patch, "abcdWXYZbcdWXYbcdWXY"u8.ToArray());
    }

    [Fact]
    public void AddressesUseAllModesAndCachesResetAcrossWindows()
    {
        byte[] dictionary = new byte[16_388];
        new Random(317).NextBytes(dictionary);
        // The first four copies fill NEAR; the next four use and replace the slots in order.
        int[] offsets = [256, 800, 1360, 1900, 260, 804, 1364, 1904, 256, 800, 1360, 0, 16384];
        byte[] instructions = Convert.FromHexString("14344454344454648474941424");
        byte[] addresses = Convert.FromHexString("820084208430841C040404040020500034");
        var patch = Patch(Window(52, [], instructions, addresses, 1, dictionary.Length),
            Window(8, [], [132, 68], [0, 0], 1, dictionary.Length));
        byte[] expected = [.. offsets.SelectMany(offset => dictionary[offset..(offset + 4)]),
            .. dictionary[..4], .. dictionary[..4]];
        AssertDecodes(dictionary, patch, expected);
    }

    [Fact]
    public void RespectsCumulativeOutputLimitAndDestinationSize()
    {
        var patch = Patch(Window(3, "abc"u8.ToArray(), [4], []), Window(3, [], [19, 3], [0], 2, 3));
        AssertDecodes([], patch, "abcabc"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, new VcdiffDecodeOptions { MaxOutputBytes = 5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Decode([], patch, new VcdiffDecodeOptions { MaxOutputBytes = -1 }));
        var output = new byte[5];
        Assert.Throws<ArgumentException>(() => Vcdiff.Decode([], patch, output));
    }

    [Fact]
    public void SameModeTreatsHighBitAsPartOfTheSingleByteIndex()
    {
        byte[] dictionary = new byte[771];
        "abcd"u8.CopyTo(dictionary.AsSpan(767));
        // Cache address 767 in SAME bank 2, slot 255. Mode 8 retrieves it using one byte.
        var patch = Patch(Window(8, [], [20, 148], [0x85, 0x7F, 0xFF], 1, dictionary.Length));
        AssertDecodes(dictionary, patch, "abcdabcd"u8.ToArray());
    }

    [Fact]
    public void LargeRunIsBoundedByTheCallerAcrossWindows()
    {
        const int count = (1 << 20) + 1;
        var patch = Patch(Window(count, [42], [0, .. Integer(count)], []), Window(1, [1], [2], []));
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, new VcdiffDecodeOptions { MaxOutputBytes = count }));
        AssertDecodes([], patch, [.. Enumerable.Repeat((byte)42, count), 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerCanReuseSourceOrPatchBackingStorage(bool useLzma)
    {
        int Decode(byte[] source, byte[] patch, Span<byte> destination) => useLzma
            ? LzmaVcdiff.Decode(source, patch, destination)
            : Vcdiff.Decode(source, patch, destination);

        byte[] source = "abcdWXYZ"u8.ToArray();
        var patch = Patch(Window(8, [], [20, 20], [4, 0], 1, 8));
        Assert.Equal(8, Decode(source, patch, source));
        Assert.Equal("WXYZabcd"u8.ToArray(), source);

        var literals = Patch(Window(4, "abcd"u8.ToArray(), [5], []), Window(4, "WXYZ"u8.ToArray(), [5], []));
        Assert.Equal(8, Decode([], literals, literals.AsSpan(literals.Length - 8)));
        Assert.Equal("abcdWXYZ"u8.ToArray(), literals[^8..]);
    }

    [Fact]
    [Trait("Category", "Allocation")]
    public void LiteralAndRunSpanDecodingNeedsNoManagedAllocations()
    {
        byte[] patch = Patch(Window(7, "ab!"u8.ToArray(), [3, 0, 5], []));
        byte[] target = new byte[7];
        Vcdiff.Decode([], patch, target);

        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            Vcdiff.Decode([], patch, target);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(0, allocated);
        Assert.Equal("ab!!!!!"u8.ToArray(), target);
    }
}
