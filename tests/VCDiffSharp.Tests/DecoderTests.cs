using Xunit;
using static VCDiffSharp.Tests.WindowVectors;

namespace VCDiffSharp.Tests;

public class DecoderTests
{
    [Theory]
    [InlineData("xdelta3")]
    [InlineData("open-vcdiff")]
    public void DecodesIndependentEncoderFixture(string encoder)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        AssertDecodes(File.ReadAllBytes(Path.Combine(dir, "source.txt")),
            File.ReadAllBytes(Path.Combine(dir, encoder + ".vcdiff")), File.ReadAllBytes(Path.Combine(dir, "target.txt")));
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
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, maxOutputBytes: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vcdiff.Decode([], patch, maxOutputBytes: -1));
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
        Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch, maxOutputBytes: count));
        AssertDecodes([], patch, [.. Enumerable.Repeat((byte)42, count), 1]);
    }

    [Fact]
    public void CallerCanReuseSourceOrPatchBackingStorage()
    {
        byte[] source = "abcdWXYZ"u8.ToArray();
        var patch = Patch(Window(8, [], [20, 20], [4, 0], 1, 8));
        Assert.Equal(8, Vcdiff.Decode(source, patch, source.AsSpan()));
        Assert.Equal("WXYZabcd"u8.ToArray(), source);

        var literals = Patch(Window(4, "abcd"u8.ToArray(), [5], []), Window(4, "WXYZ"u8.ToArray(), [5], []));
        Assert.Equal(8, Vcdiff.Decode([], literals, literals.AsSpan(literals.Length - 8)));
        Assert.Equal("abcdWXYZ"u8.ToArray(), literals[^8..]);
    }
}
