using System.Buffers;

namespace VCDiffSharp;

/// <summary>Encodes and decodes VCDIFF patches.</summary>
public static class Vcdiff
{
    /// <summary>Creates a patch that reconstructs the target from the source.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
        => throw new NotImplementedException();

    /// <summary>Writes the encoded patch to a caller-provided buffer writer.</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination)
        => throw new NotImplementedException();

    /// <summary>Reconstructs the target, subject to a limit on total output bytes.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes = int.MaxValue)
        => throw new NotImplementedException();

    /// <summary>Reconstructs the target into the destination and returns the number of bytes written.</summary>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => throw new NotImplementedException();
}
