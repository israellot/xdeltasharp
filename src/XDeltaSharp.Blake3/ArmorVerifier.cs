using System.Security.Cryptography;
using global::Blake3;
#if NET10_0_OR_GREATER
using HexDigit = System.Byte;
#else
using HexDigit = System.Char;
#endif

namespace XDeltaSharp.Blake3;

/// <summary>Reads xdelta3's target/compressor[/source/compressor] metadata without buffering filenames.</summary>
internal sealed class ArmorVerifier(bool requireDigests) : IArmorVerifier
{
    // .NET 10 can decode hex directly from the ASCII bytes in the header.
    private readonly HexDigit[] _hex = new HexDigit[64];
    private int _digits = -1;
    private int _field;
    private bool _terminated;
    private byte[]? _target;
    private byte[]? _source;

    public void ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (_terminated)
            return;
        foreach (byte value in bytes)
        {
            if (value == 0)
            {
                _terminated = true;
                return;
            }
            if (value == (byte)'/')
            {
                if (_digits == 64)
                {
                    if (_field == 0) _target = Convert.FromHexString(_hex);
                    if (_field == 2) _source = Convert.FromHexString(_hex);
                }
                _digits = -1;
                if (++_field == 3)
                {
                    _terminated = true;
                    return;
                }
            }
            else if (_field != 1)
            {
                char digit = (char)value;
                if (digit == '#')
                    _digits = 0;
                else if (_digits is >= 0 and < 64 && char.IsAsciiHexDigitLower(digit))
                    _hex[_digits++] = (HexDigit)value;
                else
                    _digits = -1;
            }
        }
    }

    // xdelta3 recognizes the two-field and four-field forms only.
    private byte[]? Target => _field is 1 or 3 ? _target : null;

    public void ValidateHeader(bool usesSource)
    {
        if (!requireDigests) return;
        if (Target == null)
            throw new InvalidDataException("The patch requires a BLAKE3 target digest.");
        if ((usesSource || _field == 3) && _source == null)
            throw new InvalidDataException("The patch requires a BLAKE3 source digest.");
    }

    public void VerifySource(ReadOnlySpan<byte> bytes) => VerifyBytes(bytes, _source, "source");
    public void VerifyTarget(ReadOnlySpan<byte> bytes) => VerifyBytes(bytes, Target, "target");

    private static void VerifyBytes(ReadOnlySpan<byte> bytes, byte[]? expected, string file)
    {
        if (expected == null) return;
        Check(Hasher.Hash(bytes).AsSpan(), expected, file);
    }

    public void VerifySource(Stream source)
    {
        if (_source == null) return;
        if (!source.CanRead || !source.CanSeek)
            throw new NotSupportedException("BLAKE3 source verification requires a readable, seekable source.");
        using var hash = new Blake3HashAlgorithm();
        Check(hash.ComputeHash(source), _source, "source");
    }

    public HashAlgorithm? StartTargetHash() => Target == null ? null : new Blake3HashAlgorithm();

    public void FinishTargetHash(HashAlgorithm hash)
    {
        hash.TransformFinalBlock([], 0, 0);
        Check(hash.Hash!, Target!, "target");
    }

    private static void Check(ReadOnlySpan<byte> actual, byte[] expected, string file)
    {
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException($"xdelta3 BLAKE3 {file} digest mismatch.");
    }
}
