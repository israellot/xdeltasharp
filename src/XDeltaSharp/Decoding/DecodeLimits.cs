namespace XDeltaSharp.Decoding;

/// <summary>The size limits of one decode call, already validated by the options that produced them.</summary>
internal readonly record struct DecodeLimits(long MaxOutputBytes, int MaxWindowBytes, int MaxEncodedWindowBytes);
