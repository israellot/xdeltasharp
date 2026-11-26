using XDeltaSharp.Blake3;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

// xdelta3 3.2.2 test_armor_blake3_kat, exercised through the armored stream decoder.
// https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-test.h#L2550-L2596
public class UpstreamArmorTests
{
    [Theory]
    [InlineData(0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")]
    [InlineData(1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213")]
    [InlineData(1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7")]
    [InlineData(2048, "e776b6028c7cd22a4d0ba182a8bf62205d2ef576467e838ed6f2529b85fba24a")]
    [InlineData(3072, "b98cb0ff3623be03326b373de6b9095218513e64f1ee2edd2525c7ad1e5cffd2")]
    public void VerifiesKnownDigestAcrossUnalignedWindowBoundaries(int length, string digest)
    {
        byte[] target = new byte[length];
        for (int i = 0; i < target.Length; i++) target[i] = (byte)(i % 251);

        // End windows inside BLAKE3's 1024-byte chunks. Encode each window as a single ADD.
        byte[] windows = target.Chunk(769).SelectMany(data =>
            Window(data.Length, data, [1, .. Integer(data.Length)], [])).ToArray();
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"target#{digest}/");
        byte[] patch = [0xd6, 0xc3, 0xc4, 0, 4, .. Integer(header.Length), .. header, .. windows];
        using var input = new MemoryStream(patch);
        using var output = new MemoryStream();
        Assert.Equal(length, Vcdiff.Decode(Stream.Null, input, output, armor: Blake3Armor.Verify));
        Assert.Equal(target, output.ToArray());

        // Keep a well-formed 64-digit digest so the negative control must verify it.
        patch[Array.IndexOf(patch, (byte)'#') + 1] = digest[0] == '0' ? (byte)'1' : (byte)'0';
        input.Position = 0;
        Assert.Contains("BLAKE3 target", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, input, Stream.Null, armor: Blake3Armor.Verify)).Message);
    }
}
