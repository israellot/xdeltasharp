using XDeltaSharp.Lzma;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

public class DecoderMutationTests
{
    private const int Limit = 4096;

    private static readonly VcdiffDecodeOptions Limits = new()
    {
        MaxOutputBytes = Limit,
        MaxWindowBytes = Limit,
        MaxEncodedWindowBytes = Limit
    };

    private static readonly LzmaDecodeOptions LzmaLimits = new()
    {
        MaxOutputBytes = Limit,
        MaxWindowBytes = Limit,
        MaxEncodedWindowBytes = Limit,
        MaxExpandedSectionBytes = Limit,
        MaxDictionaryBytes = Limit
    };

    [Theory]
    [InlineData("open-vcdiff")]
    [InlineData("xdelta3")]
    [InlineData("xdelta3-checksum")]
    [InlineData("lzma-stored")]
    public void PublicDecodersAgreeOnMutatedPatches(string fixture)
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] source = File.ReadAllBytes(Path.Combine(fixtures, "source.txt"));
        bool lzma = fixture == "lzma-stored";
        byte[] seed = lzma ? StoredLzmaPatch() : File.ReadAllBytes(Path.Combine(fixtures, fixture + ".vcdiff"));
        byte[] expected = lzma ? "abc"u8.ToArray() : File.ReadAllBytes(Path.Combine(fixtures, "target.txt"));
        byte[] buffer = new byte[Limit];
        Assert.Equal(expected, Check(seed, "original"));

        // Sweep the entire wire input so short fields get the same coverage as payload bytes.
        for (int i = 0; i < seed.Length; i++)
        {
            byte[] flipped = (byte[])seed.Clone();
            for (int bit = 0; bit < 8; bit++)
            {
                flipped[i] = (byte)(seed[i] ^ (1 << bit));
                Check(flipped, $"flip bit {bit} at byte {i}");
            }
            Check([.. seed.AsSpan(0, i), .. seed.AsSpan(i + 1)], $"delete byte {i}");
            Check([.. seed.AsSpan(0, i), seed[i], .. seed.AsSpan(i)], $"duplicate byte {i}");
        }

        byte[]? Check(byte[] patch, string mutation)
        {
            string context = $"{fixture}, {mutation}";
            byte[]? allocated = DecodeOrReject(() => lzma
                ? LzmaVcdiff.Decode(source, patch, LzmaLimits)
                : Vcdiff.Decode(source, patch, Limits), context);
            byte[]? buffered = DecodeOrReject(() =>
            {
                int length = lzma
                    ? LzmaVcdiff.Decode(source, patch, buffer, LzmaLimits)
                    : Vcdiff.Decode(source, patch, buffer, Limits);
                return buffer[..length];
            }, context, callerBuffer: true);
            byte[]? streamed = DecodeOrReject(() =>
            {
                using var dictionary = new MemoryStream(source);
                using var input = new MemoryStream(patch);
                using var output = new MemoryStream();
                long length = lzma
                    ? LzmaVcdiff.Decode(dictionary, input, output, LzmaLimits)
                    : Vcdiff.Decode(dictionary, input, output, Limits);
                Assert.Equal(output.Length, length);
                return output.ToArray();
            }, context);

            Assert.True((allocated is null) == (buffered is null) && (allocated is null) == (streamed is null),
                $"Decoders disagree on accepting {context}.");
            Assert.True(allocated.AsSpan().SequenceEqual(buffered) && allocated.AsSpan().SequenceEqual(streamed),
                $"Decoders disagree on output for {context}.");
            return allocated;
        }
    }

    private static byte[]? DecodeOrReject(Func<byte[]> decode, string context, bool callerBuffer = false)
    {
        byte[]? result = null;
        var error = Record.Exception(() => { result = decode(); });
        // The span overload reports an undersized caller buffer as an argument error.
        bool rejected = error is InvalidDataException or NotSupportedException ||
            (callerBuffer && error?.GetType() == typeof(ArgumentException) && ((ArgumentException)error).ParamName == "destination");
        if (error != null && !rejected) Assert.Fail($"Unexpected exception for {context}: {error}");
        return result;
    }

    private static byte[] StoredLzmaPatch()
    {
        // xdelta3's XZ block header with a 4 KiB LZMA2 dictionary and one stored chunk.
        byte[] header = Convert.FromHexString("FD377A585A000000FF12D9410200210100000000372797D6");
        byte[] data = [3, .. header, 1, 0, 2, .. "abc"u8];
        return [0xD6, 0xC3, 0xC4, 0, 1, 2, .. Window(3, data, [4], [], compressedSections: 1)];
    }
}
