namespace XDeltaSharp.Decoding;

// The published xdelta.Lzma 1.x and 2.0 packages call these internal signatures through InternalsVisibleTo, and
// their dependency range admits a newer core. They are kept so that pairing still works. Nothing in this
// repository calls them. eng/test-packages.py runs the published 1.0.0 add-ons against the current core.
internal static partial class PatchDecoder
{
    internal static byte[] Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, maxOutputBytes, maxWindowBytes, SecondaryDecoderFactory.From(createSecondary), armor);

    internal static int Decode(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, destination, maxWindowBytes, SecondaryDecoderFactory.From(createSecondary), armor);

    internal static long Decode(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, int maxEncodedWindowBytes, Func<ISecondaryDecoder>? createSecondary, IArmorVerifier? armor = null) =>
        DecodeWithSecondary(source, patch, destination, maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes,
            SecondaryDecoderFactory.From(createSecondary), armor);

    internal static byte[] DecodeWithSecondary(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, int maxOutputBytes,
        int maxWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null) =>
        Decode(source, patch, Limits(maxOutputBytes, maxWindowBytes, Array.MaxLength), secondaryFactory, armor);

    internal static int DecodeWithSecondary(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch, Span<byte> destination,
        int maxWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null) =>
        Decode(source, patch, destination, Limits(long.MaxValue, maxWindowBytes, Array.MaxLength), secondaryFactory, armor);

    internal static long DecodeWithSecondary(Stream source, Stream patch, Stream destination, long maxOutputBytes,
        int maxWindowBytes, int maxEncodedWindowBytes, SecondaryDecoderFactory? secondaryFactory, IArmorVerifier? armor = null) =>
        Decode(source, patch, destination, Limits(maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes), secondaryFactory, armor);

    // These callers pass raw arguments, so apply the checks the option setters would have made.
    private static DecodeLimits Limits(long maxOutputBytes, int maxWindowBytes, int maxEncodedWindowBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWindowBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWindowBytes, Vcdiff.MaxTargetWindowBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEncodedWindowBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEncodedWindowBytes, Array.MaxLength);
        return new DecodeLimits(maxOutputBytes, maxWindowBytes, maxEncodedWindowBytes);
    }
}
