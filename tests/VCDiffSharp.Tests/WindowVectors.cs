using Xunit;

namespace VCDiffSharp.Tests;

internal static class WindowVectors
{
    internal static byte[] Patch(params byte[][] windows)
        => [0xD6, 0xC3, 0xC4, 0, 0, .. windows.SelectMany(window => window)];

    internal static byte[] Integer(long value)
    {
        var digits = new List<byte> { (byte)(value & 127) };
        for (value >>= 7; value > 0; value >>= 7)
            digits.Add((byte)(128 | (value & 127)));
        digits.Reverse();
        return digits.ToArray();
    }

    internal static byte[] Window(int targetLength, byte[] data, byte[] instructions, byte[] addresses,
        byte indicator = 0, long segmentLength = 0, long segmentOffset = 0)
    {
        byte[] delta = [.. Integer(targetLength), 0, .. Integer(data.Length), .. Integer(instructions.Length),
            .. Integer(addresses.Length), .. data, .. instructions, .. addresses];
        byte[] segment = indicator == 0 ? [] : [.. Integer(segmentLength), .. Integer(segmentOffset)];
        return [indicator, .. segment, .. Integer(delta.Length), .. delta];
    }

    internal static void AssertDecodes(byte[] source, byte[] patch, byte[] expected)
    {
        Assert.Equal(expected, Vcdiff.Decode(source, patch, maxOutputBytes: expected.Length));
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
