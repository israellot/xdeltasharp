using XDeltaSharp.Decoding;

namespace XDeltaSharp.Lzma;

/// <summary>Applies VCDIFF patches with optional xdelta3 LZMA secondary compression.</summary>
/// <remarks>Handles raw and compressed sections in the same patch and carries LZMA state across windows.
/// Each LZMA dictionary and each expanded section has a default 64 MiB limit.
/// The DecodeWithSecondaryLimits overloads can lower both limits.</remarks>
/// <example><code>
/// long written = LzmaVcdiff.Decode(sourceStream, patchStream, outputStream, maxOutputBytes: 1L &lt;&lt; 30);
/// </code></example>
/// <seealso cref="Vcdiff"/>
public static class LzmaVcdiff
{
    /// <summary>Allocates and returns the target from a plain or LZMA-compressed xdelta3 patch.</summary>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        int maxOutputBytes = int.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, default(VcdiffArmor), maxOutputBytes, maxWindowBytes);

    /// <summary>Reconstructs the target in a caller-provided buffer from a plain or LZMA-compressed xdelta3 patch.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes. Overlapping source or patch spans are copied before decoding.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>A failure can leave partial output in the destination.</remarks>
    /// <exception cref="ArgumentException">The destination is too small for the target.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWindowBytes"/> is outside its supported
    /// range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, a decoded length exceeds a limit, or a window
    /// checksum fails verification.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, destination, default(VcdiffArmor), maxWindowBytes);

    /// <summary>Reconstructs the target one window at a time in the output stream from a plain or LZMA-compressed
    /// xdelta3 patch.</summary>
    /// <param name="source">Source dictionary at its current position. Supply a readable, seekable stream for source
    /// copies, or <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream at the VCDIFF header. It is consumed sequentially. Each window's
    /// encoded sections are buffered as they arrive, up to <see cref="Array.MaxLength"/> bytes in total per window.
    /// Use <see cref="DecodeWithLimits"/> to lower the encoded payload limit.</param>
    /// <param name="destination">Writable output at its current position. Windows that copy from prior target output
    /// also require reading and seeking.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>Use independent streams. <see cref="Stream.Null"/> can serve as both source and destination.
    /// All streams remain open. A readable, seekable source returns to its initial position. A failure can leave completed
    /// windows in the destination.</remarks>
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
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, destination, default(VcdiffArmor), maxOutputBytes, maxWindowBytes);

    /// <summary>Allocates and returns the target from a plain or LZMA-compressed xdelta3 patch, with optional armor
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        VcdiffArmor armor, int maxOutputBytes = int.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => PatchDecoder.DecodeWithSecondary(source, patch, maxOutputBytes, maxWindowBytes, LzmaSections.Default, armor.CreateVerifier());

    /// <summary>Reconstructs the target in a caller-provided buffer from a plain or LZMA-compressed xdelta3 patch, with
    /// optional armor verification.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes. Overlapping source or patch spans are copied before decoding.</param>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffArmor armor, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => PatchDecoder.DecodeWithSecondary(source, patch, destination, maxWindowBytes, LzmaSections.Default, armor.CreateVerifier());

    /// <summary>Reconstructs the target one window at a time in the output stream from a plain or LZMA-compressed
    /// xdelta3 patch, with optional armor verification.</summary>
    /// <param name="source">Source dictionary at its current position. Supply a readable, seekable stream for source
    /// copies or source digest verification, or <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream at the VCDIFF header. It is consumed sequentially. Each window's
    /// encoded sections are buffered as they arrive, up to <see cref="Array.MaxLength"/> bytes in total per window.
    /// Use <see cref="DecodeWithLimits"/> to lower the encoded payload limit.</param>
    /// <param name="destination">Writable output at its current position. Windows that copy from prior target output
    /// also require reading and seeking.</param>
    /// <param name="armor">Pass <c>Blake3Armor.Verify</c> from the <c>xdelta.Blake3</c> package to verify recognized
    /// digests. The default value skips armor verification.</param>
    /// <param name="maxOutputBytes">Inclusive limit, in bytes, on the total reconstructed target across all windows.</param>
    /// <param name="maxWindowBytes">Inclusive limit for each decoded target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/> (64 MiB).</param>
    /// <returns>The total number of target bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>Use independent streams. <see cref="Stream.Null"/> can serve as both source and destination.
    /// All streams remain open. A readable, seekable source returns to its initial position. A failure can leave completed
    /// windows in the destination. Source digests are checked
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
    public static long Decode(Stream source, Stream patch, Stream destination, VcdiffArmor armor, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => DecodeWithLimits(source, patch, destination, Array.MaxLength, maxOutputBytes, maxWindowBytes, armor);

    /// <summary>Reconstructs the target in a stream with an explicit limit on the encoded payload of each window.</summary>
    /// <param name="source">Source dictionary at its current position. Source copies and source digest verification
    /// require a readable, seekable stream. Use <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream at the VCDIFF file header, consumed sequentially.</param>
    /// <param name="destination">Writable output at its current position. Copies from prior windows also require
    /// a readable, seekable destination.</param>
    /// <param name="maxEncodedWindowBytes">Inclusive upper bound on the combined encoded data, instruction, and
    /// address section lengths in each window, from zero through <see cref="Array.MaxLength"/>. Window framing and
    /// checksums are excluded. The decoder checks the lengths before reading or buffering the payload.</param>
    /// <param name="maxOutputBytes">Inclusive upper bound on the total reconstructed target bytes.</param>
    /// <param name="maxWindowBytes">Inclusive upper bound on each reconstructed target window, from zero through
    /// <see cref="Vcdiff.MaxTargetWindowBytes"/>.</param>
    /// <param name="armor">Optional digest verification. Pass <c>Blake3Armor.Verify</c> from <c>xdelta.Blake3</c>
    /// to verify recognized digests.</param>
    /// <returns>The total number of target bytes written.</returns>
    /// <remarks>Use independent streams. <see cref="Stream.Null"/> may serve as both source and destination.
    /// All streams remain open, and a readable, seekable source returns to its initial position.
    /// For compressed sections the limit counts their encoded bytes. LZMA dictionaries and expanded sections have separate limits.
    /// If a window exceeds a limit, earlier windows remain in the destination. A target digest mismatch can leave
    /// the entire target in the destination.</remarks>
    /// <exception cref="ArgumentNullException">A stream argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The patch is unreadable, the destination is unwritable, or a stream is
    /// used in incompatible roles.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A byte limit is outside its supported range.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, an encoded or decoded size exceeds a limit,
    /// or an integrity check fails.</exception>
    /// <exception cref="NotSupportedException">The patch uses an unsupported format feature or a dictionary stream
    /// lacks the required capabilities.</exception>
    public static long DecodeWithLimits(Stream source, Stream patch, Stream destination, int maxEncodedWindowBytes,
        long maxOutputBytes = long.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes, VcdiffArmor armor = default)
        => PatchDecoder.DecodeWithSecondary(source, patch, destination, maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes,
            LzmaSections.Default, armor.CreateVerifier());

    // The three overloads have distinct required arguments for array, span, and stream output.
#pragma warning disable RS0026
    /// <summary>Returns a new target array with explicit limits on LZMA section expansion and dictionaries.</summary>
    /// <param name="source">Original dictionary bytes, or an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete VCDIFF patch bytes.</param>
    /// <param name="maxExpandedSectionBytes">Inclusive limit on each expanded LZMA section, from zero through 64 MiB.</param>
    /// <param name="maxDictionaryBytes">Inclusive limit on each declared LZMA dictionary, from zero through 64 MiB.</param>
    /// <param name="maxOutputBytes">Inclusive limit on the total reconstructed target bytes.</param>
    /// <param name="maxWindowBytes">Inclusive limit on each target window, from zero through 64 MiB.</param>
    /// <param name="armor">Optional digest verification.</param>
    /// <remarks>Data, instructions, and addresses have independent buffers and dictionaries.
    /// A dictionary can retain history across target windows.</remarks>
    /// <exception cref="InvalidDataException">The patch is malformed, fails an integrity check, or exceeds a limit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A byte limit is outside its supported range.</exception>
    public static byte[] DecodeWithSecondaryLimits(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        int maxExpandedSectionBytes, int maxDictionaryBytes, int maxOutputBytes = int.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes, VcdiffArmor armor = default)
        => PatchDecoder.DecodeWithSecondary(source, patch, maxOutputBytes, maxWindowBytes,
            LzmaSections.WithLimits(maxExpandedSectionBytes, maxDictionaryBytes), armor.CreateVerifier());

    /// <summary>Decodes into a caller-provided buffer with explicit limits on LZMA section expansion and dictionaries.</summary>
    /// <param name="source">Original dictionary bytes, or an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete VCDIFF patch bytes.</param>
    /// <param name="destination">Buffer with room for the entire target.</param>
    /// <param name="maxExpandedSectionBytes">Inclusive limit on each expanded LZMA section, from zero through 64 MiB.</param>
    /// <param name="maxDictionaryBytes">Inclusive limit on each declared LZMA dictionary, from zero through 64 MiB.</param>
    /// <param name="maxWindowBytes">Inclusive limit on each target window, from zero through 64 MiB.</param>
    /// <param name="armor">Optional digest verification.</param>
    /// <returns>The number of target bytes written.</returns>
    /// <remarks>A failure can leave partial output in the destination.</remarks>
    /// <exception cref="InvalidDataException">The patch is malformed, fails an integrity check, or exceeds a limit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A byte limit is outside its supported range.</exception>
    public static int DecodeWithSecondaryLimits(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxExpandedSectionBytes, int maxDictionaryBytes, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes,
        VcdiffArmor armor = default)
        => PatchDecoder.DecodeWithSecondary(source, patch, destination, maxWindowBytes,
            LzmaSections.WithLimits(maxExpandedSectionBytes, maxDictionaryBytes), armor.CreateVerifier());

    /// <summary>Decodes a stream one window at a time with limits on encoded payload, LZMA expansion, and dictionaries.</summary>
    /// <param name="source">Original seekable dictionary stream, or <see cref="Stream.Null"/> for a dictionary-free patch.</param>
    /// <param name="patch">Readable patch stream positioned at the VCDIFF header.</param>
    /// <param name="destination">Writable output stream. Prior-target copies also require reading and seeking.</param>
    /// <param name="maxEncodedWindowBytes">Inclusive limit on the combined encoded section sizes per window.</param>
    /// <param name="maxExpandedSectionBytes">Inclusive limit on each expanded LZMA section, from zero through 64 MiB.</param>
    /// <param name="maxDictionaryBytes">Inclusive limit on each declared LZMA dictionary, from zero through 64 MiB.</param>
    /// <param name="maxOutputBytes">Inclusive limit on the total reconstructed target bytes.</param>
    /// <param name="maxWindowBytes">Inclusive limit on each target window, from zero through 64 MiB.</param>
    /// <param name="armor">Optional digest verification.</param>
    /// <returns>The number of target bytes written.</returns>
    /// <remarks>Streams remain open. A failure can leave completed windows in the destination.</remarks>
    /// <exception cref="InvalidDataException">The patch is malformed, fails an integrity check, or exceeds a limit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A byte limit is outside its supported range.</exception>
    public static long DecodeWithSecondaryLimits(Stream source, Stream patch, Stream destination, int maxEncodedWindowBytes,
        int maxExpandedSectionBytes, int maxDictionaryBytes, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes, VcdiffArmor armor = default)
        => PatchDecoder.DecodeWithSecondary(source, patch, destination, maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes,
            LzmaSections.WithLimits(maxExpandedSectionBytes, maxDictionaryBytes), armor.CreateVerifier());
#pragma warning restore RS0026
}
