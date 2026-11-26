using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class InvalidPatchTests
{
    [Theory]
    [InlineData("D6C3C4000102", "secondary")]
    [InlineData("D6C3C40001FF", "secondary")]
    [InlineData("D6C3C40002", "code tables")]
    [InlineData("D6C3C45300", "version")]
    public void RecognizedUnsupportedFeaturesAreReported(string hex, string feature)
    {
        var patch = Convert.FromHexString(hex);
        var error = Assert.Throws<NotSupportedException>(() => Vcdiff.Decode([], patch));
        Assert.Contains(feature, error.Message);
        Assert.Throws<NotSupportedException>(() => Vcdiff.Decode([], patch, new byte[8]));
    }

    [Fact]
    public void RejectsTruncatedSingleWindowAtEveryByte()
    {
        byte[] patch = Patch(Window(5, [(byte)'!'], [163], [0], 1, 4));
        for (int end = 0; end < patch.Length; end++)
        {
            if (end == 5) continue; // A complete header can encode an empty target.
            AssertInvalid(patch[..end]);
        }
    }

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "signature", Convert.FromHexString("D6C3000000") },
        { "reserved file flag", Convert.FromHexString("D6C3C40008") },
        { "missing application header length", Convert.FromHexString("D6C3C40004") },
        { "missing secondary compressor ID", Convert.FromHexString("D6C3C40001") },
        { "truncated application header", Convert.FromHexString("D6C3C4000403FF00") },
        { "truncated checksum", Patch([4, 9, 0, 0, 0, 0, 0, 0, 0, 0]) },
        { "delta length omits checksum", Patch([4, 5, 0, 0, 0, 0, 0, 0, 0, 0, 1]) },
        { "reserved window flag", Patch([0x80]) },
        { "source and prior-target flags together", Patch([3]) },
        { "compressed section without declared compressor", Patch([0, 5, 0, 1, 0, 0, 0]) },
        { "reserved delta flag", Patch([0, 5, 0, 8, 0, 0, 0]) },
        { "extra bytes in the delta envelope", Patch([0, 6, 0, 0, 0, 0, 0, 0]) },
        { "unavailable prior target", Patch(Window(0, [], [], [], 2, 1)) },
        { "source segment out of range", Patch(Window(0, [], [], [], 1, 2, 3)) },
        { "segment end arithmetic overflow", Patch(Window(0, [], [], [], 1, 3, int.MaxValue)) },
        { "integer wider than 32 bits", Patch([0, 0x90, 0x80, 0x80, 0x80, 0]) },
        { "length exceeds signed span range", Patch([0, 0x88, 0x80, 0x80, 0x80, 0]) },
        { "missing explicit size", Patch(Window(1, [42], [1], [])) },
        { "unterminated explicit size", Patch(Window(1, [42], [1, 0x80], [])) },
        { "explicit size overflow", Patch(Window(0, [], [1, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], [])) },
        { "literal exceeds data section", Patch(Window(2, [42], [3], [])) },
        { "RUN lacks value", Patch(Window(3, [], [0, 3], [])) },
        { "output too short", Patch(Window(2, [42], [2], [])) },
        { "output too long", Patch(Window(1, [42], [0, 2], [])) },
        { "trailing data bytes", Patch(Window(1, [42, 43], [2], [])) },
        { "trailing address bytes", Patch(Window(1, [42], [2], [0])) },
        { "trailing instruction", Patch(Window(1, [42], [2, 2], [])) },
        { "missing SAME operand", Patch(Window(4, [], [116], [], 1, 4)) },
        { "missing second instruction operand", Patch(Window(5, [42], [163], [], 1, 4)) },
        { "COPY at current write position", Patch(Window(4, [], [20], [0])) },
        { "COPY beyond current write position", Patch(Window(4, [], [20], [5], 1, 4)) },
        { "COPY spans dictionary boundary", Patch(Window(5, [42], [2, 20], [1], 1, 4)) },
        { "HERE underflow", Patch(Window(4, [], [36], [5], 1, 4)) },
        { "NEAR addition exceeds unsigned range", Patch(Window(2, [], [19, 1, 51, 1],
            [1, 0x8F, 0xFF, 0xFF, 0xFF, 0x7F], 1, 4)) }
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void RejectsMalformedPatch(string reason, byte[] patch) => AssertInvalid(patch, reason);

    private static void AssertInvalid(byte[] patch, string reason = "truncated window")
    {
        byte[] source = [0, 1, 2, 3];
        var allocating = Record.Exception(() => Vcdiff.Decode(source, patch, maxOutputBytes: 100));
        var intoBuffer = Record.Exception(() => Vcdiff.Decode(source, patch, new byte[100]));
        var streamed = Record.Exception(() => Vcdiff.Decode(new MemoryStream(source), new MemoryStream(patch),
            Stream.Null, maxOutputBytes: 100));
        Assert.True(allocating is InvalidDataException, $"Allocating overload, {reason}. Received {allocating}");
        Assert.True(intoBuffer is InvalidDataException, $"Span overload, {reason}. Received {intoBuffer}");
        Assert.True(streamed is InvalidDataException, $"Stream overload, {reason}. Received {streamed}");
    }
}
