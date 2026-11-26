namespace XDeltaSharp.Decoding;

/// <summary>A selected source or previous-target segment.</summary>
internal ref struct WindowDictionary
{
    internal const int PageSize = 4096;
    private readonly Span<byte> _page;
    private long _cachedPage;
    private long _lastPage;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly Stream? _stream;
    private readonly long _start;
    internal long Length { get; }

    internal WindowDictionary(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        Length = bytes.Length;
    }

    internal WindowDictionary(Stream stream, long start, long length, Span<byte> page)
    {
        _stream = stream;
        _start = start;
        Length = length;
        _page = stream is FileStream ? page : [];
        _cachedPage = _lastPage = -1;
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

        int withinPage = (int)(address & (PageSize - 1));
        if (!_page.IsEmpty && destination.Length <= PageSize - withinPage)
        {
            long pageStart = address - withinPage;
            // Read ahead only after two nearby requests. Scattered COPYs keep reading directly.
            if (pageStart != _cachedPage && pageStart == _lastPage)
            {
                Read(pageStart, _page[..(int)Math.Min(PageSize, Length - pageStart)]);
                _cachedPage = pageStart;
            }
            _lastPage = pageStart;
            if (_cachedPage == pageStart)
            {
                _page.Slice(withinPage, destination.Length).CopyTo(destination);
                return;
            }
        }
        Read(address, destination);
    }

    private readonly void Read(long address, Span<byte> destination)
    {
        _stream!.Position = _start + address;
        _stream.ReadExactly(destination);
    }
}
