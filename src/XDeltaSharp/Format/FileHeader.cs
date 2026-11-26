namespace XDeltaSharp.Format;

internal static class FileHeader
{
    internal const byte SignatureByte1 = 0xD6;
    internal const byte SignatureByte2 = 0xC3;
    internal const byte SignatureByte3 = 0xC4;
    internal const byte Version = 0;
    internal const byte XdeltaLzmaCompressorId = 2;

    internal static ReadOnlySpan<byte> Default => [SignatureByte1, SignatureByte2, SignatureByte3, Version, (byte)FileHeaderFlags.None];
}

// File and window indicators assign different meanings to the same bits.
[Flags]
internal enum FileHeaderFlags
{
    None = 0,
    SecondaryCompression = 1,
    CustomCodeTable = 2,
    ApplicationHeader = 4,
    KnownMask = SecondaryCompression | CustomCodeTable | ApplicationHeader
}

[Flags]
internal enum WindowFlags
{
    None = 0,
    Source = 1,
    PriorTarget = 2,
    Adler32 = 4,
    DictionaryMask = Source | PriorTarget,
    KnownMask = DictionaryMask | Adler32
}
