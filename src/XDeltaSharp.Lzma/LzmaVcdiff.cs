namespace XDeltaSharp.Lzma;

/// <summary>Applies VCDIFF patches with optional xdelta3 LZMA secondary compression.</summary>
/// <remarks>Handles raw and compressed sections in the same patch and carries LZMA state across windows.
/// Each LZMA dictionary and each expanded section has a 64 MiB limit.</remarks>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, destination, default(VcdiffArmor), maxWindowBytes);

    /// <summary>Reconstructs the target one window at a time in the output stream from a plain or LZMA-compressed
    /// xdelta3 patch.</summary>
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
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, maxOutputBytes, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }

    /// <summary>Reconstructs the target in a caller-provided buffer from a plain or LZMA-compressed xdelta3 patch, with
    /// optional armor verification.</summary>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as a custom code
    /// table or a different secondary compressor.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffArmor armor, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, destination, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }

    /// <summary>Reconstructs the target one window at a time in the output stream from a plain or LZMA-compressed
    /// xdelta3 patch, with optional armor verification.</summary>
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
    public static long Decode(Stream source, Stream patch, Stream destination, VcdiffArmor armor, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }
}
