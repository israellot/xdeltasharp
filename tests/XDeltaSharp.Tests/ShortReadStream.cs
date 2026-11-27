namespace XDeltaSharp.Tests;

/// <summary>A forward-only stream that returns at most <paramref name="maxRead"/> bytes per read, as a
/// socket might.</summary>
internal sealed class ShortReadStream(byte[] bytes, int maxRead) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(maxRead, buffer.Length)]);
}
