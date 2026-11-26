using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using XDeltaSharp.Format;

namespace XDeltaSharp;

/// <summary>Creates and applies RFC 3284 version 0 patches using the default code table and uncompressed sections.</summary>
/// <remarks>The encoder uses 8 MiB target windows. Decoding verifies any xdelta3 Adler-32 window checksums.
/// For xdelta3 LZMA secondary compression, use <c>LzmaVcdiff.Decode</c> from the <c>xdelta.Lzma</c> package.</remarks>
/// <example><code>
/// byte[] patch = Vcdiff.Encode(original, updated);
/// byte[] restored = Vcdiff.Decode(original, patch, maxOutputBytes: updated.Length);
/// </code></example>
/// <seealso cref="VcdiffEncodeMode"/>
/// <seealso cref="MemoryMode"/>
public static class Vcdiff
{
    /// <summary>Creates a VCDIFF patch using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
        => Encode(source, target, VcdiffEncodeMode.Default);

    /// <summary>Appends a VCDIFF patch to a buffer writer using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination)
        => Encode(source, target, destination, VcdiffEncodeMode.Default);

    /// <summary>Creates a VCDIFF patch using the selected match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is outside its supported range.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeMode mode)
        => Encode(source, target, mode, MemoryMode.Default);

    /// <summary>Creates a VCDIFF patch using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="memoryMode"/> is outside its supported range.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, MemoryMode memoryMode)
        => Encode(source, target, VcdiffEncodeMode.Default, memoryMode);

    /// <summary>Creates a VCDIFF patch using the selected match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> or <paramref name="memoryMode"/> is
    /// outside its supported range.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeMode mode,
        MemoryMode memoryMode)
    {
        ValidateEncodeModes(mode, memoryMode);
        return WindowEncoder.Encode(source, target, null, mode, memoryMode)!;
    }

    /// <summary>Appends a VCDIFF patch to a buffer writer using the selected match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is outside its supported range.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode)
        => Encode(source, target, destination, mode, MemoryMode.Default);

    /// <summary>Appends a VCDIFF patch to a buffer writer using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="memoryMode"/> is outside its supported range.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        MemoryMode memoryMode)
        => Encode(source, target, destination, VcdiffEncodeMode.Default, memoryMode);

    /// <summary>Appends a VCDIFF patch to a buffer writer using the selected match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> or <paramref name="memoryMode"/> is
    /// outside its supported range.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode, MemoryMode memoryMode)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateEncodeModes(mode, memoryMode);
        WindowEncoder.Encode(source, target, destination, mode, memoryMode);
    }

    /// <summary>Creates a VCDIFF patch at the specified compression level.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. Higher settings allow more search effort.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, int compressionLevel,
        MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateMemoryMode(memoryMode);
        return WindowEncoder.Encode(source, target, null, VcdiffEncodeMode.Default, memoryMode, compressionLevel)!;
    }

    /// <summary>Appends a VCDIFF patch to a buffer writer at the specified compression level.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. Higher settings allow more search effort.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        int compressionLevel, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateMemoryMode(memoryMode);
        WindowEncoder.Encode(source, target, destination, VcdiffEncodeMode.Default, memoryMode, compressionLevel);
    }

    /// <summary>Creates a VCDIFF patch using the selected match search, refining each window for size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> or <paramref name="memoryMode"/> is
    /// outside its supported range.</exception>
    public static byte[] EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target,
        VcdiffEncodeMode mode = VcdiffEncodeMode.Default, MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateEncodeModes(mode, memoryMode);
        return WindowEncoder.Encode(source, target, null, mode, memoryMode, optimize: true)!;
    }

    /// <summary>Appends a VCDIFF patch to a buffer writer using the selected match search, refining each window for
    /// size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="mode">Match-search strategy. <see cref="VcdiffEncodeMode.Default"/> uses level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> or <paramref name="memoryMode"/> is
    /// outside its supported range.</exception>
    public static void EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeMode mode = VcdiffEncodeMode.Default, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateEncodeModes(mode, memoryMode);
        WindowEncoder.Encode(source, target, destination, mode, memoryMode, optimize: true);
    }

    /// <summary>Creates a VCDIFF patch at the specified compression level, refining each window for size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. Higher settings allow more search effort.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
    public static byte[] EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, int compressionLevel,
        MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateMemoryMode(memoryMode);
        return WindowEncoder.Encode(source, target, null, VcdiffEncodeMode.Default, memoryMode, compressionLevel, true)!;
    }

    /// <summary>Appends a VCDIFF patch to a buffer writer at the specified compression level, refining each window for
    /// size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. Higher settings allow more search effort.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
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

    /// <summary>Maximum decoded size of a single target window, in bytes (64 MiB).</summary>
    /// <remarks>A multi-window patch may reconstruct a larger file. Set <c>maxOutputBytes</c> to bound the whole
    /// target.</remarks>
    public const int MaxTargetWindowBytes = 64 * 1024 * 1024;

    /// <summary>Allocates and returns the target from a VCDIFF patch with uncompressed sections.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <returns>A new byte array containing the reconstructed target.</returns>
    /// <remarks>Checks the total length before allocating the target array.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputBytes"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes = int.MaxValue)
        => Decode(source, patch, maxOutputBytes, MaxTargetWindowBytes);

    /// <summary>Allocates and returns the target from a VCDIFF patch with uncompressed sections.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>A new byte array containing the reconstructed target.</returns>
    /// <remarks>Checks the total length before allocating the target array.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputBytes"/> is negative or
    /// <paramref name="maxWindowBytes"/> is outside its supported range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes, int maxWindowBytes)
        => Decode(source, patch, maxOutputBytes, maxWindowBytes, null);

    /// <summary>Allocates and returns the target from a VCDIFF patch with uncompressed sections, with optional armor
    /// verification.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="armor">Pass <c>Blake3Armor.Verify</c> from the <c>xdelta.Blake3</c> package to verify recognized
    /// digests. The default value skips armor verification.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>A new byte array containing the reconstructed target.</returns>
    /// <remarks>Checks the total length before allocating the target array. Source digests are checked before output
    /// and the target digest after reconstruction.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputBytes"/> is negative or
    /// <paramref name="maxWindowBytes"/> is outside its supported range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or an integrity
    /// check fails.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        VcdiffArmor armor, int maxOutputBytes = int.MaxValue, int maxWindowBytes = MaxTargetWindowBytes)
        => Decode(source, patch, maxOutputBytes, maxWindowBytes, null, armor.CreateVerifier());

    internal static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, DecodeSecondarySection? secondary, IArmorVerifier? armor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ValidateWindowLimit(maxWindowBytes);
        var reader = new WindowReader(patch, maxWindowBytes, secondary != null, armor);
        armor?.VerifySource(source);
        var sizing = reader;
        int total = 0;
        int limit = Math.Min(maxOutputBytes, Array.MaxLength);
        while (sizing.TryReadWindow(source.Length, total, limit - total, out var window))
            total += window.TargetLength;

        var result = new byte[total];
        DecodeWindows(reader, source, result, secondary);
        armor?.VerifyTarget(result);
        return result;
    }

    /// <summary>Reconstructs the target in a caller-provided buffer from a VCDIFF patch with uncompressed sections.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes.</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>A failure can leave partial output in the destination.</remarks>
    /// <exception cref="ArgumentException">The destination is too small for the target.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => Decode(source, patch, destination, MaxTargetWindowBytes);

    /// <summary>Reconstructs the target in a caller-provided buffer from a VCDIFF patch with uncompressed sections.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>A failure can leave partial output in the destination.</remarks>
    /// <exception cref="ArgumentException">The destination is too small for the target.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWindowBytes"/> is outside its supported
    /// range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination, int maxWindowBytes)
        => Decode(source, patch, destination, maxWindowBytes, null);

    /// <summary>Reconstructs the target in a caller-provided buffer from a VCDIFF patch with uncompressed sections,
    /// with optional armor verification.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes.</param>
    /// <param name="armor">Pass <c>Blake3Armor.Verify</c> from the <c>xdelta.Blake3</c> package to verify recognized
    /// digests. The default value skips armor verification.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>A failure can leave partial output in the destination. Source digests are checked before output and the
    /// target digest after reconstruction. A target digest mismatch can leave the full target in the
    /// destination.</remarks>
    /// <exception cref="ArgumentException">The destination is too small for the target.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWindowBytes"/> is outside its supported
    /// range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or an integrity
    /// check fails.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffArmor armor, int maxWindowBytes = MaxTargetWindowBytes)
        => Decode(source, patch, destination, maxWindowBytes, null, armor.CreateVerifier());

    internal static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, DecodeSecondarySection? secondary, IArmorVerifier? armor = null)
    {
        ValidateWindowLimit(maxWindowBytes);
        if (source.Overlaps(destination))
            source = source.ToArray();
        if (patch.Overlaps(destination))
            patch = patch.ToArray();

        var reader = new WindowReader(patch, maxWindowBytes, secondary != null, armor);
        armor?.VerifySource(source);
        int written = DecodeWindows(reader, source, destination, secondary);
        armor?.VerifyTarget(destination[..written]);
        return written;
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

    /// <summary>Reconstructs the target one window at a time in the output stream from a VCDIFF patch with uncompressed
    /// sections.</summary>
    /// <param name="source">Source dictionary at its current position. Supply a readable, seekable stream for source
    /// copies, or <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream at the VCDIFF header. It is consumed sequentially.</param>
    /// <param name="destination">Writable output at its current position. Windows that copy from prior target output
    /// also require reading and seeking.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>All streams remain open. Source offsets are relative to the initial position, which is restored for a
    /// readable, seekable source. A failure can leave completed windows in the destination.</remarks>
    /// <exception cref="ArgumentNullException">A stream argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The patch is unreadable, the destination is unwritable, or the same stream
    /// is used in incompatible roles.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputBytes"/> is negative or
    /// <paramref name="maxWindowBytes"/> is outside its supported range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature or a dictionary stream
    /// lacks the required capabilities.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = MaxTargetWindowBytes)
        => Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, null);

    /// <summary>Reconstructs the target one window at a time in the output stream from a VCDIFF patch with uncompressed
    /// sections, with optional armor verification.</summary>
    /// <param name="source">Source dictionary at its current position. Supply a readable, seekable stream for source
    /// copies or source digest verification, or <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream at the VCDIFF header. It is consumed sequentially.</param>
    /// <param name="destination">Writable output at its current position. Windows that copy from prior target output
    /// also require reading and seeking.</param>
    /// <param name="armor">Pass <c>Blake3Armor.Verify</c> from the <c>xdelta.Blake3</c> package to verify recognized
    /// digests. The default value skips armor verification.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>All streams remain open. Source offsets are relative to the initial position, which is restored for a
    /// readable, seekable source. A failure can leave completed windows in the destination. Source digests are checked
    /// before output and the target digest after reconstruction. A target digest mismatch can leave the full target in
    /// the destination.</remarks>
    /// <exception cref="ArgumentNullException">A stream argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The patch is unreadable, the destination is unwritable, or the same stream
    /// is used in incompatible roles.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputBytes"/> is negative or
    /// <paramref name="maxWindowBytes"/> is outside its supported range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or an integrity
    /// check fails.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature or a dictionary stream
    /// lacks the required capabilities.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination,
        VcdiffArmor armor, long maxOutputBytes = long.MaxValue, int maxWindowBytes = MaxTargetWindowBytes)
        => Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, null, armor.CreateVerifier());

    internal static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, DecodeSecondarySection? secondary, IArmorVerifier? armor = null)
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
        var reader = new WindowReader(patch, maxWindowBytes, secondary != null, armor);
        var cache = new AddressCache();
        byte[] buffer = [];
        long written = 0;
        try
        {
            armor?.VerifySource(source);
            using var targetHash = armor?.StartTargetHash();
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
                targetHash?.TransformBlock(buffer, 0, target.Length, null, 0);
                destination.Write(target);
                written += target.Length;
            }
            if (targetHash != null)
                armor!.FinishTargetHash(targetHash);
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
