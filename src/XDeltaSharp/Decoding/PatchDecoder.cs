using XDeltaSharp.Format;

namespace XDeltaSharp.Decoding;

/// <summary>Applies patches to each supported destination using the same window reader and instruction decoder.</summary>
internal static partial class PatchDecoder
{
    internal static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, DecodeLimits limits,
        SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor)
    {
        var reader = new WindowReader(patch, limits.MaxWindowBytes, secondaryFactory != null, armor);
        armor?.VerifySource(source);

        // Read the window headers once to size the result before allocating it.
        var sizing = reader;
        int total = 0;
        int limit = (int)Math.Min(limits.MaxOutputBytes, Array.MaxLength);
        while (sizing.TryReadWindow(source.Length, total, limit - total, out var window))
            total += window.TargetLength;

        var result = new byte[total];
        DecodeWindows(reader, source, result, long.MaxValue, secondaryFactory);
        armor?.VerifyTarget(result);
        return result;
    }

    internal static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        DecodeLimits limits, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor)
    {
        if (source.Overlaps(destination))
            source = source.ToArray();
        if (patch.Overlaps(destination))
            patch = patch.ToArray();

        var reader = new WindowReader(patch, limits.MaxWindowBytes, secondaryFactory != null, armor);
        armor?.VerifySource(source);
        int written = DecodeWindows(reader, source, destination, limits.MaxOutputBytes, secondaryFactory);
        armor?.VerifyTarget(destination[..written]);
        return written;
    }

    private static int DecodeWindows(WindowReader reader, ReadOnlySpan<byte> source, Span<byte> destination,
        long maxOutputBytes, SecondaryDecoderFactory? secondaryFactory)
    {
        AddressCache? cache = null;
        ISecondaryDecoder? secondary = null;
        int written = 0;
        try
        {
            while (reader.TryReadWindow(source.Length, written, maxOutputBytes - written, out var window))
            {
                if (window.TargetLength > destination.Length - written)
                    throw new ArgumentException("Destination is too small for the target.", nameof(destination));

                ReadOnlySpan<byte> bytes = window.UsePriorTarget ? destination[..written] : source;
                var dictionary = new WindowDictionary(bytes.Slice((int)window.SegmentOffset, (int)window.SegmentLength));
                var decoder = new WindowDecoder(window, dictionary, destination.Slice(written, window.TargetLength),
                    secondaryFactory, ref secondary);
                decoder.Decode(ref cache);
                written += window.TargetLength;
            }
        }
        finally
        {
            secondary?.Dispose();
        }
        return written;
    }

    internal static long Decode(Stream source, Stream patch, Stream destination, DecodeLimits limits,
        SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(destination);
        ValidateStreams(source, patch, destination);

        bool canReadSource = source.CanRead && source.CanSeek;
        long sourceStart = canReadSource ? source.Position : 0;
        long sourceLength = canReadSource ? Math.Max(0, source.Length - sourceStart) : -1;
        long outputStart = destination.CanSeek ? destination.Position : 0;
        bool canReadPriorTarget = destination.CanRead && destination.CanSeek;
        long limit = Math.Min(limits.MaxOutputBytes, long.MaxValue - outputStart);
        var reader = new WindowReader(patch, limits.MaxWindowBytes, limits.MaxEncodedWindowBytes, secondaryFactory != null, armor);
        AddressCache? cache = null;
        ISecondaryDecoder? secondary = null;
        byte[] buffer = [];
        Span<byte> page = source is FileStream || destination is FileStream ? stackalloc byte[WindowDictionary.PageSize] : [];
        long written = 0;
        try
        {
            armor?.VerifySource(source);
            using var targetHash = armor?.StartTargetHash();
            while (reader.TryReadWindow(sourceLength, canReadPriorTarget ? written : -1, limit - written, out var window))
            {
                if (buffer.Length < window.TargetLength)
                    buffer = new byte[window.TargetLength];
                var target = buffer.AsSpan(0, window.TargetLength);
                var dictionary = window.UsePriorTarget
                    ? new WindowDictionary(destination, outputStart + window.SegmentOffset, window.SegmentLength, page)
                    : new WindowDictionary(source, sourceStart + window.SegmentOffset, window.SegmentLength, page);
                var decoder = new WindowDecoder(window, dictionary, target, secondaryFactory, ref secondary);
                try
                {
                    decoder.Decode(ref cache);
                }
                finally
                {
                    if (window.UsePriorTarget)
                        destination.Position = outputStart + written;
                }
                targetHash?.TransformBlock(buffer, 0, target.Length, null, 0);
                destination.Write(target);
                written += target.Length;
            }
            if (targetHash != null)
                armor!.FinishTargetHash(targetHash);
        }
        finally
        {
            try
            {
                if (canReadSource)
                    source.Position = sourceStart;
            }
            finally
            {
                secondary?.Dispose();
            }
        }
        return written;
    }

    private static void ValidateStreams(Stream source, Stream patch, Stream destination)
    {
        if (!patch.CanRead)
            throw new ArgumentException("The patch stream must be readable.", nameof(patch));
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        if (ReferenceEquals(source, patch))
            throw new ArgumentException("The patch stream must be independent of the source.", nameof(patch));
        if (ReferenceEquals(patch, destination) || (ReferenceEquals(source, destination) && source != Stream.Null))
            throw new ArgumentException("The destination stream must be independent of the inputs.", nameof(destination));
    }
}
