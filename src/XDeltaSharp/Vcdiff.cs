using System.Buffers;
using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;

namespace XDeltaSharp;

/// <summary>Creates and applies VCDIFF (RFC 3284) patches.</summary>
/// <remarks>
/// Patches use format version 0, the default code table and uncompressed sections, which is what
/// <c>xdelta3 -S none</c> writes. The decoder also verifies xdelta3's Adler-32 window checksums. To read
/// LZMA secondary compression, use <c>LzmaVcdiff</c> from the <c>xdelta.Lzma</c> package.
/// </remarks>
/// <example><code>
/// byte[] patch = Vcdiff.Encode(original, updated);
/// byte[] restored = Vcdiff.Decode(original, patch);
/// </code></example>
public static class Vcdiff
{
    /// <summary>The largest target window the decoder accepts, in bytes (64 MiB).</summary>
    /// <remarks>A patch can hold any number of windows. <see cref="VcdiffDecodeOptions.MaxOutputBytes"/> bounds
    /// the whole target.</remarks>
    public const int MaxTargetWindowBytes = 64 * 1024 * 1024;

    /// <summary>Creates a patch that rebuilds <paramref name="target"/> from <paramref name="source"/>.</summary>
    /// <param name="source">The bytes the recipient already has. Pass an empty span to encode without them.</param>
    /// <param name="target">The bytes the patch must reproduce.</param>
    /// <returns>The complete patch.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
        => Encode(source, target, VcdiffEncodeOptions.Default);

    /// <summary>Creates a patch that rebuilds <paramref name="target"/> from <paramref name="source"/>.</summary>
    /// <param name="source">The bytes the recipient already has. Pass an empty span to encode without them.</param>
    /// <param name="target">The bytes the patch must reproduce.</param>
    /// <param name="options">The compression level, index size and optional size pass.</param>
    /// <returns>The complete patch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, VcdiffEncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return WindowEncoder.Encode(source, target, options);
    }

    /// <summary>Appends a patch that rebuilds <paramref name="target"/> from <paramref name="source"/> to
    /// <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes the recipient already has. Pass an empty span to encode without them.</param>
    /// <param name="target">The bytes the patch must reproduce.</param>
    /// <param name="destination">Receives the file header and then each window as it is finished.</param>
    /// <remarks>The writer's memory must not overlap <paramref name="source"/> or <paramref name="target"/>.
    /// If the writer throws, it may already hold the windows written before the failure.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination)
        => Encode(source, target, destination, VcdiffEncodeOptions.Default);

    /// <summary>Appends a patch that rebuilds <paramref name="target"/> from <paramref name="source"/> to
    /// <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes the recipient already has. Pass an empty span to encode without them.</param>
    /// <param name="target">The bytes the patch must reproduce.</param>
    /// <param name="destination">Receives the file header and then each window as it is finished.</param>
    /// <param name="options">The compression level, index size and optional size pass.</param>
    /// <remarks>The writer's memory must not overlap <paramref name="source"/> or <paramref name="target"/>.
    /// If the writer throws, it may already hold the windows written before the failure.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="options"/> is
    /// <see langword="null"/>.</exception>
    public static void Encode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, IBufferWriter<byte> destination,
        VcdiffEncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        WindowEncoder.Encode(source, target, destination, options);
    }

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> and returns the target.</summary>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <returns>The reconstructed target.</returns>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
        => Decode(source, patch, VcdiffDecodeOptions.Default);

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> and returns the target.</summary>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <param name="options">Size limits and digest verification.</param>
    /// <returns>The reconstructed target.</returns>
    /// <remarks>The target length is checked against <see cref="VcdiffDecodeOptions.MaxOutputBytes"/> before the
    /// array is allocated.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum or
    /// digest.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, VcdiffDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, options.Limits, secondaryFactory: null, options.Armor.CreateVerifier());
    }

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> and writes the target to
    /// <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <param name="destination">A buffer large enough for the whole target. It may overlap
    /// <paramref name="source"/> or <paramref name="patch"/>, which are then copied first.</param>
    /// <returns>The number of bytes written to the start of <paramref name="destination"/>.</returns>
    /// <remarks>A failed decode can leave partial output in <paramref name="destination"/>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small for the target.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => Decode(source, patch, destination, VcdiffDecodeOptions.Default);

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> and writes the target to
    /// <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <param name="destination">A buffer large enough for the whole target. It may overlap
    /// <paramref name="source"/> or <paramref name="patch"/>, which are then copied first.</param>
    /// <param name="options">Size limits and digest verification.</param>
    /// <returns>The number of bytes written to the start of <paramref name="destination"/>.</returns>
    /// <remarks>A failed decode can leave partial output in <paramref name="destination"/>. A target digest is
    /// checked last, so a mismatch leaves the whole target there.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small for the target.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum or
    /// digest.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, secondaryFactory: null,
            options.Armor.CreateVerifier());
    }

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> one window at a time and writes
    /// the target to <paramref name="destination"/>.</summary>
    /// <param name="source">The stream the patch was created against, read from its current position. It must be
    /// readable and seekable if the patch copies from it. Pass <see cref="Stream.Null"/> if the patch has no
    /// source.</param>
    /// <param name="patch">The patch, read forward from its file header. It does not need to seek.</param>
    /// <param name="destination">Receives the target at its current position. It must also be readable and
    /// seekable if a window copies from earlier output.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>The three streams must be distinct and are left open. A seekable source is returned to its
    /// starting position. A failed decode leaves the windows already written in
    /// <paramref name="destination"/>.</remarks>
    /// <exception cref="ArgumentNullException">A stream is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The patch cannot be read, the destination cannot be written, or two
    /// arguments are the same stream.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table, or
    /// copies from a stream that cannot seek.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination)
        => Decode(source, patch, destination, VcdiffDecodeOptions.Default);

    /// <summary>Applies <paramref name="patch"/> to <paramref name="source"/> one window at a time and writes
    /// the target to <paramref name="destination"/>.</summary>
    /// <param name="source">The stream the patch was created against, read from its current position. It must be
    /// readable and seekable if the patch copies from it or carries a source digest to verify. Pass
    /// <see cref="Stream.Null"/> if the patch has no source.</param>
    /// <param name="patch">The patch, read forward from its file header. It does not need to seek.</param>
    /// <param name="destination">Receives the target at its current position. It must also be readable and
    /// seekable if a window copies from earlier output.</param>
    /// <param name="options">Size limits and digest verification.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <remarks>The three streams must be distinct and are left open. A seekable source is returned to its
    /// starting position. A failed decode leaves the windows already written in <paramref name="destination"/>.
    /// A target digest is checked last, so a mismatch leaves the whole target there.</remarks>
    /// <exception cref="ArgumentNullException">A stream or <paramref name="options"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The patch cannot be read, the destination cannot be written, or two
    /// arguments are the same stream.</exception>
    /// <exception cref="InvalidDataException">The patch is malformed, exceeds a limit, or fails a checksum or
    /// digest.</exception>
    /// <exception cref="NotSupportedException">The patch uses secondary compression or a custom code table, or
    /// copies from a stream that cannot seek.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination, VcdiffDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, secondaryFactory: null,
            options.Armor.CreateVerifier());
    }
}
