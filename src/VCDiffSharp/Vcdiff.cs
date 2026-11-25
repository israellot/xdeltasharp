using System.Buffers;
using VCDiffSharp.Decoding;
using VCDiffSharp.Format;

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
    /// <exception cref="InvalidDataException">The patch is malformed or exceeds the output limit.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        var reader = new WindowReader(patch);
        var sizing = reader;
        int total = 0;
        int limit = Math.Min(maxOutputBytes, Array.MaxLength);
        while (!sizing.IsEmpty)
        {
            var window = sizing.ReadWindow(source.Length, total);
            if (window.TargetLength > limit - total)
                throw new InvalidDataException("The target exceeds the output limit.");
            total += window.TargetLength;
        }

        var result = new byte[total];
        DecodeWindows(reader, source, result);
        return result;
    }

    /// <summary>Reconstructs the target into the destination and returns the number of bytes written.</summary>
    /// <remarks>If decoding fails, the destination may contain partial output.</remarks>
    /// <exception cref="ArgumentException">The destination has insufficient capacity.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
    {
        // Preserve input bytes if the caller reuses their backing storage for the result.
        if (source.Overlaps(destination))
            source = source.ToArray();
        if (patch.Overlaps(destination))
            patch = patch.ToArray();

        return DecodeWindows(new WindowReader(patch), source, destination);
    }

    private static int DecodeWindows(WindowReader reader, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var cache = new AddressCache();
        int written = 0;
        while (!reader.IsEmpty)
        {
            var window = reader.ReadWindow(source.Length, written);
            if (window.TargetLength > destination.Length - written)
                throw new ArgumentException("Destination is too small for the target.", nameof(destination));

            ReadOnlySpan<byte> dictionary = window.UsePriorTarget ? destination[..written] : source;
            var decoder = new WindowDecoder(window, dictionary.Slice(window.SegmentOffset, window.SegmentLength),
                destination.Slice(written, window.TargetLength), cache);
            decoder.Decode();
            written += window.TargetLength;
        }
        return written;
    }
}
