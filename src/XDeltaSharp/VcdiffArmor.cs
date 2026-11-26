using System.Security.Cryptography;

namespace XDeltaSharp;

/// <summary>Selects optional application-header verification. An option can be shared across decode calls.</summary>
/// <remarks>The default value skips armor verification. Use <c>Blake3Armor.Verify</c> from <c>xdelta.Blake3</c>
/// to check whole-file digests when present. Adler-32 window checksums are verified independently.</remarks>
public readonly struct VcdiffArmor
{
    private readonly Func<IArmorVerifier>? _createVerifier;

    internal VcdiffArmor(Func<IArmorVerifier> createVerifier) => _createVerifier = createVerifier;
    internal IArmorVerifier? CreateVerifier() => _createVerifier?.Invoke();
}

internal interface IArmorVerifier
{
    void ReadHeader(ReadOnlySpan<byte> bytes);
    void VerifySource(ReadOnlySpan<byte> bytes);
    void VerifySource(Stream source);
    void VerifyTarget(ReadOnlySpan<byte> bytes);
    HashAlgorithm? StartTargetHash();
    void FinishTargetHash(HashAlgorithm hash);
}
