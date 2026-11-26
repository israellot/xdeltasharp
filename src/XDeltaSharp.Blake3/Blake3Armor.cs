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
    public static VcdiffArmor Verify { get; } = new(static () => new ArmorVerifier(requireDigests: false));

    /// <summary>Requires a BLAKE3 target digest and verifies it after reconstruction. A patch that
    /// names a source in its header or declares a source dictionary must also carry a source digest.</summary>
    /// <value>A reusable option for all decode overloads, including LZMA.</value>
    /// <remarks>A missing required digest raises <see cref="InvalidDataException"/>. A target-only
    /// header is sufficient for a dictionary-free patch. Embedded digests check integrity. To
    /// authenticate the publisher, verify the patch against a trusted signature.</remarks>
    public static VcdiffArmor Require { get; } = new(static () => new ArmorVerifier(requireDigests: true));
}
