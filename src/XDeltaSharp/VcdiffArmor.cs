using System.Security.Cryptography;

namespace XDeltaSharp;

/// <summary>Selects optional application-header verification. An option can be shared across decode calls.</summary>
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
