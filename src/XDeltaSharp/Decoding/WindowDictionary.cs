namespace XDeltaSharp.Decoding;

/// <summary>A selected source or previous-target segment. Stream-backed COPY reads only the requested bytes.</summary>
internal readonly ref struct WindowDictionary
{
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly Stream? _stream;
    private readonly long _start;
    internal long Length { get; }

    internal WindowDictionary(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        Length = bytes.Length;
    }

    internal WindowDictionary(Stream stream, long start, long length)
    {
        _stream = stream;
        _start = start;
        Length = length;
    }

    internal void Copy(long address, Span<byte> destination)
    {
        if (destination.Length > Length - address)
            throw new InvalidDataException("COPY extends beyond the selected dictionary.");
        if (_stream == null)
        {
            _bytes.Slice((int)address, destination.Length).CopyTo(destination);
            return;
        }

        _stream.Position = _start + address;
        _stream.ReadExactly(destination);
    }
}
