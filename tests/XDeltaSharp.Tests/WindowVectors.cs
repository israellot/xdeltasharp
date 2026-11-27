using Xunit;

namespace XDeltaSharp.Tests;

internal static class WindowVectors
{
    // The VCDIFF magic bytes and format version 0. The file header's flag byte follows.
    private static ReadOnlySpan<byte> Signature => [0xD6, 0xC3, 0xC4, 0];

    internal static byte[] Patch(params byte[][] windows)
        => [.. Signature, 0, .. windows.SelectMany(window => window)];

    /// <summary>A patch whose file header declares LZMA, compressor ID 2, for its sections.</summary>
    internal static byte[] LzmaPatch(params byte[][] windows)
        => [.. Signature, 1, 2, .. windows.SelectMany(window => window)];

    internal static byte[] PatchWithApplicationHeader(byte[] header, params byte[][] windows)
        => [.. Signature, 4, .. Integer(header.Length), .. header, .. windows.SelectMany(window => window)];

    internal static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    internal static byte[] Integer(long value)
    {
        var digits = new List<byte> { (byte)(value & 127) };
        for (value >>= 7; value > 0; value >>= 7)
            digits.Add((byte)(128 | (value & 127)));
        digits.Reverse();
        return digits.ToArray();
    }

    internal static byte[] Window(int targetLength, byte[] data, byte[] instructions, byte[] addresses,
        byte indicator = 0, long segmentLength = 0, long segmentOffset = 0, uint? checksum = null,
        byte compressedSections = 0)
    {
        byte[] checksumBytes = [];
        byte[] segment = indicator == 0 ? [] : [.. Integer(segmentLength), .. Integer(segmentOffset)];
        if (checksum is uint value)
        {
            indicator |= 4;
            checksumBytes = [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }
        byte[] delta = [.. Integer(targetLength), compressedSections, .. Integer(data.Length), .. Integer(instructions.Length),
            .. Integer(addresses.Length), .. checksumBytes, .. data, .. instructions, .. addresses];
        return [indicator, .. segment, .. Integer(delta.Length), .. delta];
    }

    internal static VcdiffEncodeOptions Level(int level, MemoryMode memory = MemoryMode.Default, bool optimize = false)
        => new() { CompressionLevel = level, MemoryMode = memory, Optimize = optimize };

    internal static void AssertDecodes(byte[] source, byte[] patch, byte[] expected)
    {
        Assert.Equal(expected, Vcdiff.Decode(source, patch, new VcdiffDecodeOptions { MaxOutputBytes = expected.Length }));
        byte[] buffer = Enumerable.Repeat((byte)0xCC, expected.Length + 2).ToArray();
        Assert.Equal(expected.Length, Vcdiff.Decode(source, patch, buffer.AsSpan(1)));
        Assert.Equal(expected, buffer[1..^1]);
        Assert.Equal(0xCC, buffer[0]);
        Assert.Equal(0xCC, buffer[^1]);

        using var output = new MemoryStream();
        Assert.Equal(expected.LongLength, Vcdiff.Decode(new MemoryStream(source), new MemoryStream(patch), output));
        Assert.Equal(expected, output.ToArray());
    }
}
