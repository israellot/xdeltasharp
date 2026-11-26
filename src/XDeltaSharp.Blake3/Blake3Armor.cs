namespace XDeltaSharp.Blake3;

/// <summary>Enables verification of the BLAKE3 digests in xdelta3 application headers.</summary>
public static class Blake3Armor
{
    /// <summary>Verifies source and target digests when present. Unarmored patches also decode.</summary>
    public static VcdiffArmor Verify { get; } = new(static () => new ArmorVerifier());
}
