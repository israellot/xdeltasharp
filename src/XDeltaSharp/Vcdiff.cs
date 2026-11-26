using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;

namespace XDeltaSharp;

/// <summary>Creates and applies RFC 3284 version 0 patches using the default code table and uncompressed sections.</summary>
/// <remarks>The encoder uses 8 MiB target windows. Decoding verifies any xdelta3 Adler-32 window checksums.
/// The buffer-writer overloads append completed windows to caller-owned storage. Keep the source and target
/// separate from the writer's output storage until encoding returns. If the writer throws, it may already contain
/// earlier windows. For xdelta3 LZMA secondary compression, use <c>LzmaVcdiff.Decode</c> from the
/// <c>xdelta.Lzma</c> package.</remarks>
/// <example><code>
/// byte[] patch = Vcdiff.Encode(original, updated);
/// byte[] restored = Vcdiff.Decode(original, patch, maxOutputBytes: updated.Length);
/// </code></example>
/// <seealso cref="MemoryMode"/>
public static class Vcdiff
{
    /// <summary>Creates a VCDIFF patch using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
        => Encode(source, target, MatcherProfile.DefaultLevel);

    /// <summary>Appends a VCDIFF patch to a buffer writer using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination)
        => Encode(source, target, destination, MatcherProfile.DefaultLevel);

    /// <summary>Creates a VCDIFF patch using the default level-3 match search.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="memoryMode"/> is outside its supported range.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, MemoryMode memoryMode)
        => Encode(source, target, MatcherProfile.DefaultLevel, memoryMode);

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
        => Encode(source, target, destination, MatcherProfile.DefaultLevel, memoryMode);

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
        return WindowEncoder.Encode(source, target, compressionLevel, memoryMode);
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
        WindowEncoder.Encode(source, target, destination, compressionLevel, memoryMode);
    }

    /// <summary>Creates a VCDIFF patch at the specified compression level, refining each window for size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. The default is level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <returns>A new byte array containing the complete patch.</returns>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
    public static byte[] EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target,
        int compressionLevel = MatcherProfile.DefaultLevel, MemoryMode memoryMode = MemoryMode.Default)
    {
        ValidateMemoryMode(memoryMode);
        return WindowEncoder.Encode(source, target, compressionLevel, memoryMode, optimize: true);
    }

    /// <summary>Appends a VCDIFF patch to a buffer writer at the specified compression level, refining each window for
    /// size.</summary>
    /// <param name="source">Original bytes that the decoder will use as its dictionary. Pass an empty span to encode
    /// without an external dictionary.</param>
    /// <param name="target">New bytes to reconstruct when the patch is applied.</param>
    /// <param name="destination">Writer that receives the header and encoded windows in order.</param>
    /// <param name="compressionLevel">Search level from 0 through 9, inclusive. The default is level 3.</param>
    /// <param name="memoryMode">Match-index sizing policy. <see cref="MemoryMode.LessMemory"/> uses fewer hash buckets.</param>
    /// <remarks>Spends extra encoding time and temporary memory to refine each window. A candidate replaces the
    /// ordinary encoding only when its complete window is smaller.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compressionLevel"/> or
    /// <paramref name="memoryMode"/> is outside its supported range.</exception>
    public static void EncodeOptimized(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        int compressionLevel = MatcherProfile.DefaultLevel, MemoryMode memoryMode = MemoryMode.Default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateMemoryMode(memoryMode);
        WindowEncoder.Encode(source, target, destination, compressionLevel, memoryMode, optimize: true);
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

    /// <summary>Allocates and returns the target using the default output and window limits.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <returns>A new byte array containing the reconstructed target.</returns>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum.</exception>
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
        => Decode(source, patch, int.MaxValue, MaxTargetWindowBytes);

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
    [SuppressMessage("ApiDesign", "RS0026", Justification =
        "The exact two- and three-argument overloads preserve existing calls. Named maxWindowBytes selects this overload.")]
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        int maxOutputBytes = int.MaxValue, int maxWindowBytes = MaxTargetWindowBytes)
        => PatchDecoder.Decode(source, patch, maxOutputBytes, maxWindowBytes, null);

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
        => PatchDecoder.Decode(source, patch, maxOutputBytes, maxWindowBytes, null, armor.CreateVerifier());

    /// <summary>Reconstructs the target in a caller-provided buffer from a VCDIFF patch with uncompressed sections.</summary>
    /// <param name="source">Original dictionary bytes. Pass an empty span for a dictionary-free patch.</param>
    /// <param name="patch">Complete patch bytes, including the VCDIFF file header.</param>
    /// <param name="destination">Writable buffer with room for the entire target. Only the returned prefix contains
    /// reconstructed bytes. Overlapping source or patch spans are copied before decoding.</param>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination, int maxWindowBytes)
        => PatchDecoder.Decode(source, patch, destination, maxWindowBytes, null);

    /// <summary>Reconstructs the target in a caller-provided buffer from a VCDIFF patch with uncompressed sections,
    /// with optional armor verification.</summary>
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
    /// <exception cref="NotSupportedException">The patch requires an unsupported format feature, such as secondary
    /// compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffArmor armor, int maxWindowBytes = MaxTargetWindowBytes)
        => PatchDecoder.Decode(source, patch, destination, maxWindowBytes, null, armor.CreateVerifier());

    /// <summary>Reconstructs the target one window at a time in the output stream from a VCDIFF patch with uncompressed
    /// sections.</summary>
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
        int maxWindowBytes = MaxTargetWindowBytes)
        => PatchDecoder.Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, Array.MaxLength, null);

    /// <summary>Reconstructs the target one window at a time in the output stream from a VCDIFF patch with uncompressed
    /// sections, with optional armor verification.</summary>
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
    public static long Decode(Stream source, Stream patch, Stream destination,
        VcdiffArmor armor, long maxOutputBytes = long.MaxValue, int maxWindowBytes = MaxTargetWindowBytes)
        => PatchDecoder.Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, Array.MaxLength, null, armor.CreateVerifier());

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
        => PatchDecoder.Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes,
            null, armor.CreateVerifier());
}
