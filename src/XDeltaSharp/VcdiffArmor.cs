using System.Security.Cryptography;

namespace XDeltaSharp;

/// <summary>Selects how <see cref="VcdiffDecodeOptions.Armor"/> checks the digests in a patch's application
/// header.</summary>
/// <remarks>The default value skips them. Use <c>Blake3Armor.Verify</c> from <c>xdelta.Blake3</c> to check
/// digests when present, or <c>Blake3Armor.Require</c> to reject a patch without them. Adler-32 window
/// checksums are always verified.</remarks>
public readonly struct VcdiffArmor
{
    private readonly Func<IArmorVerifier>? _createVerifier;

    internal VcdiffArmor(Func<IArmorVerifier> createVerifier) => _createVerifier = createVerifier;
    internal IArmorVerifier? CreateVerifier() => _createVerifier?.Invoke();
}

internal interface IArmorVerifier
{
    void ReadHeader(ReadOnlySpan<byte> bytes);
    // The 1.0.0 BLAKE3 add-on implements optional verification only.
    void ValidateHeader(bool usesSource) { }
    void VerifySource(ReadOnlySpan<byte> bytes);
    void VerifySource(Stream source);
    void VerifyTarget(ReadOnlySpan<byte> bytes);
    HashAlgorithm? StartTargetHash();
    void FinishTargetHash(HashAlgorithm hash);
}
