using XDeltaSharp.Decoding;

namespace XDeltaSharp.Lzma;

/// <summary>Applies VCDIFF patches whose sections may use xdelta3's LZMA secondary compression.</summary>
/// <remarks>
/// This is the decoder for patches written by a default <c>xdelta3</c> build. Uncompressed patches work too, and
/// a patch may mix raw and compressed sections. LZMA is the only secondary compressor it reads. Everything said
/// about sources, destinations, limits and failures on the matching <see cref="Vcdiff"/> method applies here.
/// </remarks>
/// <example><code>
/// long written = LzmaVcdiff.Decode(original, patch, restored,
///     new LzmaDecodeOptions { MaxOutputBytes = 1L &lt;&lt; 30 });
/// </code></example>
public static class LzmaVcdiff
{
    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, VcdiffDecodeOptions)"/>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
        => Decode(source, patch, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, VcdiffDecodeOptions)"/>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, options.Limits, options.Sections, options.Armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, VcdiffDecodeOptions)"/>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination)
        => Decode(source, patch, destination, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, VcdiffDecodeOptions)"/>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, options.Sections,
            options.Armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream, VcdiffDecodeOptions)"/>
    public static long Decode(Stream source, Stream patch, Stream destination)
        => Decode(source, patch, destination, LzmaDecodeOptions.Default);

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream, VcdiffDecodeOptions)"/>
    public static long Decode(Stream source, Stream patch, Stream destination, LzmaDecodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return PatchDecoder.Decode(source, patch, destination, options.Limits, options.Sections,
            options.Armor.CreateVerifier());
    }
}
