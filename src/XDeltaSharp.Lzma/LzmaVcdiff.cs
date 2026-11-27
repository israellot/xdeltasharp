using XDeltaSharp.Decoding;

namespace XDeltaSharp.Lzma;

/// <summary>Applies VCDIFF patches whose sections may use xdelta3's LZMA secondary compression.</summary>
/// <remarks>
/// This is the decoder for patches written by a default <c>xdelta3</c> build. Uncompressed patches work too, and
/// a patch may mix raw and compressed sections. Everything said about sources, destinations, limits and failures
/// on the matching <see cref="Vcdiff"/> method applies here.
/// </remarks>
/// <example><code>
/// long written = LzmaVcdiff.Decode(original, patch, restored,
///     new LzmaDecodeOptions { MaxOutputBytes = 1L &lt;&lt; 30 });
/// </code></example>
public static class LzmaVcdiff
{
    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA, or a custom
    /// code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
        => Decode(source, patch, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, VcdiffDecodeOptions)"/>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <param name="options">Size limits, LZMA limits and digest verification.</param>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA, or a custom
    /// code table.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, options.Limits, options.Sections, options.Armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte})"/>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA, or a custom
    /// code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => Decode(source, patch, destination, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, VcdiffDecodeOptions)"/>
    /// <param name="source">The bytes the patch was created against. Pass an empty span if it had none.</param>
    /// <param name="patch">The complete patch, starting at its file header.</param>
    /// <param name="destination">A buffer large enough for the whole target. It may overlap
    /// <paramref name="source"/> or <paramref name="patch"/>, which are then copied first.</param>
    /// <param name="options">Size limits, LZMA limits and digest verification.</param>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA, or a custom
    /// code table.</exception>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, options.Sections,
            options.Armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream)"/>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA or a custom
    /// code table, or copies from a stream that cannot seek.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination)
        => Decode(source, patch, destination, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream, VcdiffDecodeOptions)"/>
    /// <param name="source">The stream the patch was created against, read from its current position. It must be
    /// readable and seekable if the patch copies from it or carries a source digest to verify. Pass
    /// <see cref="Stream.Null"/> if the patch has no source.</param>
    /// <param name="patch">The patch, read forward from its file header. It does not need to seek.</param>
    /// <param name="destination">Receives the target at its current position. It must also be readable and
    /// seekable if a window copies from earlier output.</param>
    /// <param name="options">Size limits, LZMA limits and digest verification.</param>
    /// <exception cref="NotSupportedException">The patch uses a secondary compressor other than LZMA or a custom
    /// code table, or copies from a stream that cannot seek.</exception>
    public static long Decode(Stream source, Stream patch, Stream destination, LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, options.Sections,
            options.Armor.CreateVerifier());
    }
}
