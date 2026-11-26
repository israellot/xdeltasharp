using XDeltaSharp.Format;

namespace XDeltaSharp.Decoding;

/// <summary>Applies patches to each supported destination using the same window reader and instruction decoder.</summary>
internal static class PatchDecoder
{
    // xdelta.Lzma 1.0.0 calls these three signatures through InternalsVisibleTo.
    internal static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, maxOutputBytes, maxWindowBytes, SecondaryDecoderFactory.From(createSecondary), armor);

    internal static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, destination, maxWindowBytes, SecondaryDecoderFactory.From(createSecondary), armor);

    internal static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, int maxEncodedWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, destination, maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes,
            SecondaryDecoderFactory.From(createSecondary), armor);

    internal static byte[] DecodeWithSecondary(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ValidateWindowLimit(maxWindowBytes);
        var reader = new WindowReader(patch, maxWindowBytes, secondaryFactory != null, armor);
        armor?.VerifySource(source);
        var sizing = reader;
        int total = 0;
        int limit = Math.Min(maxOutputBytes, Array.MaxLength);
        while (sizing.TryReadWindow(source.Length, total, limit - total, out var window))
            total += window.TargetLength;

        var result = new byte[total];
        DecodeWindows(reader, source, result, secondaryFactory);
        armor?.VerifyTarget(result);
        return result;
    }

    internal static int DecodeWithSecondary(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null)
    {
        ValidateWindowLimit(maxWindowBytes);
        if (source.Overlaps(destination))
            source = source.ToArray();
        if (patch.Overlaps(destination))
            patch = patch.ToArray();

        var reader = new WindowReader(patch, maxWindowBytes, secondaryFactory != null, armor);
        armor?.VerifySource(source);
        int written = DecodeWindows(reader, source, destination, secondaryFactory);
        armor?.VerifyTarget(destination[..written]);
        return written;
    }

    private static int DecodeWindows(WindowReader reader, ReadOnlySpan<byte> source, Span<byte> destination,
        SecondaryDecoderFactory? secondaryFactory)
    {
        AddressCache? cache = null;
        ISecondaryDecoder? secondary = null;
        int written = 0;
        try
        {
            while (reader.TryReadWindow(source.Length, written, long.MaxValue, out var window))
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

    internal static long DecodeWithSecondary(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, int maxEncodedWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ValidateWindowLimit(maxWindowBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEncodedWindowBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEncodedWindowBytes, Array.MaxLength);
        ValidateStreams(source, patch, destination);

        bool canReadSource = source.CanRead && source.CanSeek;
        long sourceStart = canReadSource ? source.Position : 0;
        long sourceLength = canReadSource ? Math.Max(0, source.Length - sourceStart) : -1;
        long outputStart = destination.CanSeek ? destination.Position : 0;
        bool canReadPriorTarget = destination.CanRead && destination.CanSeek;
        long limit = Math.Min(maxOutputBytes, long.MaxValue - outputStart);
        var reader = new WindowReader(patch, maxWindowBytes, maxEncodedWindowBytes, secondaryFactory != null, armor);
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

    private static void ValidateWindowLimit(int maxWindowBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxWindowBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWindowBytes, Vcdiff.MaxTargetWindowBytes);
    }
}
