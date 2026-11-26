using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using XDeltaSharp.Format;

namespace XDeltaSharp;

/// <summary>Encodes and decodes VCDIFF patches.</summary>
public static class Vcdiff
{
    /// <summary>Creates a patch that reconstructs the target from the source.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
        => Encode(source, target, VcdiffEncodeMode.Default);

    /// <summary>Writes the encoded patch to a caller-provided buffer writer.</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination)
        => Encode(source, target, destination, VcdiffEncodeMode.Default);

    /// <summary>Creates a patch using the selected search mode.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeMode mode)
        => Encode(source, target, mode, MemoryMode.Default);

    /// <summary>Creates a patch using the specified source-index memory policy.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, MemoryMode memoryMode)
        => Encode(source, target, VcdiffEncodeMode.Default, memoryMode);

    /// <summary>Creates a patch using the selected search and memory policies.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeMode mode,
        MemoryMode memoryMode)
    {
        ValidateEncodeModes(mode, memoryMode);
        return WindowEncoder.Encode(source, target, null, mode, memoryMode)!;
    }

    /// <summary>Appends a VCDIFF file, one framed window at a time.</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode)
        => Encode(source, target, destination, mode, MemoryMode.Default);

    /// <summary>Appends a patch using the specified source-index memory policy.</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        MemoryMode memoryMode)
        => Encode(source, target, destination, VcdiffEncodeMode.Default, memoryMode);

    /// <summary>Appends a patch using the selected search and memory policies.</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode, MemoryMode memoryMode)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateEncodeModes(mode, memoryMode);
        WindowEncoder.Encode(source, target, destination, mode, memoryMode);
    }

    /// <summary>Creates a patch using xdelta3-style compression levels 0 through 9. The default level is 3.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, int compressionLevel,
        MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateMemoryMode(memoryMode);
        return WindowEncoder.Encode(source, target, null, VcdiffEncodeMode.Default, memoryMode, compressionLevel)!;
    }

    /// <summary>Appends a patch using the selected compression level (0 through 9).</summary>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        int compressionLevel, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateMemoryMode(memoryMode);
        WindowEncoder.Encode(source, target, destination, VcdiffEncodeMode.Default, memoryMode, compressionLevel);
    }

    /// <summary>Encodes and refines each window, keeping its smaller representation.</summary>
    public static byte[] EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target,
        VcdiffEncodeMode mode = VcdiffEncodeMode.Default, MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateEncodeModes(mode, memoryMode);
        return WindowEncoder.Encode(source, target, null, mode, memoryMode, optimize: true)!;
    }

    /// <summary>Appends size-refined windows to a caller-provided buffer writer.</summary>
    public static void EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode = VcdiffEncodeMode.Default, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateEncodeModes(mode, memoryMode);
        WindowEncoder.Encode(source, target, destination, mode, memoryMode, optimize: true);
    }

    /// <summary>Encodes at the specified level and refines the selected window operations.</summary>
    public static byte[] EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, int compressionLevel,
        MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateMemoryMode(memoryMode);
        return WindowEncoder.Encode(source, target, null, VcdiffEncodeMode.Default, memoryMode, compressionLevel, true)!;
    }

    /// <summary>Appends size-refined windows using the specified compression level.</summary>
    public static void EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        int compressionLevel, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateMemoryMode(memoryMode);
        WindowEncoder.Encode(source, target, destination, VcdiffEncodeMode.Default, memoryMode, compressionLevel, true);
    }

    private static void ValidateEncodeModes(VcdiffEncodeMode mode, MemoryMode memoryMode)
    {
        if (mode is not (VcdiffEncodeMode.Fast or VcdiffEncodeMode.Default or VcdiffEncodeMode.Exhaustive)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateMemoryMode(memoryMode);
    }

    private static void ValidateMemoryMode(MemoryMode memoryMode)
    {
        if (memoryMode is not (MemoryMode.Default or MemoryMode.LessMemory))
            throw new ArgumentOutOfRangeException(nameof(memoryMode));
    }

    /// <summary>The largest decoded target window accepted by the library, matching xdelta3's 64 MiB limit.</summary>
    public const int MaxTargetWindowBytes = 64 * 1024 * 1024;

    /// <summary>Reconstructs the target, subject to a limit on total output bytes.</summary>
    /// <exception cref="InvalidDataException">The patch is malformed or exceeds an output limit.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes = int.MaxValue)
        => Decode(source, patch, maxOutputBytes, MaxTargetWindowBytes);

    /// <summary>Reconstructs the target with separate cumulative and per-window output limits.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes, int maxWindowBytes)
        => Decode(source, patch, maxOutputBytes, maxWindowBytes, null);

    internal static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, DecodeSecondarySection? secondary)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ValidateWindowLimit(maxWindowBytes);
        var reader = new WindowReader(patch, maxWindowBytes, secondary != null);
        var sizing = reader;
        int total = 0;
        int limit = Math.Min(maxOutputBytes, Array.MaxLength);
        while (sizing.TryReadWindow(source.Length, total, limit - total, out var window))
            total += window.TargetLength;

        var result = new byte[total];
        DecodeWindows(reader, source, result, secondary);
        return result;
    }

    /// <summary>Reconstructs the target into the destination and returns the number of bytes written.</summary>
    /// <remarks>If decoding fails, the destination may contain partial output.</remarks>
    /// <exception cref="ArgumentException">The destination has insufficient capacity.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed or exceeds the window limit.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => Decode(source, patch, destination, MaxTargetWindowBytes);

    /// <summary>Reconstructs the target into the destination with a caller-specified target window limit.</summary>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination, int maxWindowBytes)
        => Decode(source, patch, destination, maxWindowBytes, null);

    internal static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, DecodeSecondarySection? secondary)
    {
        ValidateWindowLimit(maxWindowBytes);
        if (source.Overlaps(destination))
            source = source.ToArray();
        if (patch.Overlaps(destination))
            patch = patch.ToArray();

        return DecodeWindows(new WindowReader(patch, maxWindowBytes, secondary != null), source, destination, secondary);
    }

    private static int DecodeWindows(WindowReader reader, ReadOnlySpan<byte> source, Span<byte> destination,
        DecodeSecondarySection? secondary)
    {
        var cache = new AddressCache();
        int written = 0;
        while (reader.TryReadWindow(source.Length, written, long.MaxValue, out var window))
        {
            if (window.TargetLength > destination.Length - written)
                throw new ArgumentException("Destination is too small for the target.", nameof(destination));

            ReadOnlySpan<byte> bytes = window.UsePriorTarget ? destination[..written] : source;
            var dictionary = new WindowDictionary(bytes.Slice((int)window.SegmentOffset, (int)window.SegmentLength));
            var decoder = new WindowDecoder(window, dictionary, destination.Slice(written, window.TargetLength), cache, secondary);
            decoder.Decode();
            written += window.TargetLength;
        }
        return written;
    }

    /// <summary>Decodes sequential patch windows and returns the total number of bytes written.</summary>
    /// <remarks>Offsets are relative to the streams' initial positions. Source dictionaries require a
    /// readable, seekable source. Prior-target dictionaries require a readable, seekable destination.
    /// Streams remain open, and previously completed windows may have been written on failure.</remarks>
    public static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = MaxTargetWindowBytes)
        => Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, null);

    internal static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, DecodeSecondarySection? secondary)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ValidateWindowLimit(maxWindowBytes);
        if (!patch.CanRead || !destination.CanWrite)
            throw new ArgumentException("Decode requires a readable patch and a writable destination.");
        if (ReferenceEquals(patch, destination) || ReferenceEquals(patch, source) ||
            (ReferenceEquals(source, destination) && source != Stream.Null))
            throw new ArgumentException("Use independent streams for the source, patch and destination.");

        long sourceStart = source.CanSeek ? source.Position : 0;
        long sourceLength = source.CanSeek && source.CanRead ? Math.Max(0, source.Length - sourceStart) : -1;
        long outputStart = destination.CanSeek ? destination.Position : 0;
        long limit = Math.Min(maxOutputBytes, long.MaxValue - outputStart);
        var reader = new WindowReader(patch, maxWindowBytes, secondary != null);
        var cache = new AddressCache();
        byte[] buffer = [];
        long written = 0;
        try
        {
            while (reader.TryReadWindow(sourceLength, written, limit - written, out var window))
            {
                if (buffer.Length < window.TargetLength)
                    buffer = new byte[window.TargetLength];
                var target = buffer.AsSpan(0, window.TargetLength);
                if (window.UsePriorTarget && (!destination.CanRead || !destination.CanSeek))
                    throw new NotSupportedException("Prior-target dictionaries require a readable, seekable destination.");
                var dictionary = window.UsePriorTarget
                    ? new WindowDictionary(destination, outputStart + window.SegmentOffset, window.SegmentLength)
                    : new WindowDictionary(source, sourceStart + window.SegmentOffset, window.SegmentLength);
                var decoder = new WindowDecoder(window, dictionary, target, cache, secondary);
                try
                {
                    decoder.Decode();
                }
                finally
                {
                    if (window.UsePriorTarget)
                        destination.Position = outputStart + written;
                }
                destination.Write(target);
                written += target.Length;
            }
        }
        finally
        {
            if (sourceLength >= 0)
                source.Position = sourceStart;
        }
        return written;
    }

    private static void ValidateWindowLimit(int maxWindowBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxWindowBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWindowBytes, MaxTargetWindowBytes);
    }
}
