namespace XDeltaSharp.Lzma;

/// <summary>Applies VCDIFF patches with optional xdelta3 LZMA secondary compression.</summary>
public static class LzmaVcdiff
{
    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, int)"/>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        int maxOutputBytes = int.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, default(VcdiffArmor), maxOutputBytes, maxWindowBytes);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, int)"/>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, destination, default(VcdiffArmor), maxWindowBytes);

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream, long, int)"/>
    public static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
        => Decode(source, patch, destination, default(VcdiffArmor), maxOutputBytes, maxWindowBytes);

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, VcdiffArmor, int, int)"/>
    public static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch,
        VcdiffArmor armor, int maxOutputBytes = int.MaxValue, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, maxOutputBytes, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, VcdiffArmor, int)"/>
    public static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        VcdiffArmor armor, int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, destination, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }

    /// <inheritdoc cref="Vcdiff.Decode(Stream, Stream, Stream, VcdiffArmor, long, int)"/>
    public static long Decode(Stream source, Stream patch, Stream destination, VcdiffArmor armor, long maxOutputBytes = long.MaxValue,
        int maxWindowBytes = Vcdiff.MaxTargetWindowBytes)
    {
        using var sections = new LzmaSections();
        return Vcdiff.Decode(source, patch, destination, maxOutputBytes, maxWindowBytes, sections.Decode, armor.CreateVerifier());
    }
}
