namespace XDeltaSharp.Blake3;

/// <summary>Enables verification of the BLAKE3 digests in xdelta3 application headers.</summary>
/// <example><code>
/// byte[] target = Vcdiff.Decode(source, patch, armor: Blake3Armor.Verify);
/// </code></example>
/// <seealso cref="VcdiffArmor"/>
public static class Blake3Armor
{
    /// <summary>Verifies source and target digests when present. Unarmored patches also decode.</summary>
    /// <value>A reusable option for the allocating, span, and stream overloads of either decoder.</value>
    /// <remarks>A source mismatch raises <see cref="InvalidDataException"/> before output begins.
    /// A target mismatch is reported after reconstruction. Verification of a source digest requires
    /// a readable, seekable source stream.</remarks>
    public static VcdiffArmor Verify { get; } = new(static () => new ArmorVerifier());
}
