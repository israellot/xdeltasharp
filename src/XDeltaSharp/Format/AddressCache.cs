using XDeltaSharp.Decoding;

namespace XDeltaSharp.Format;

/// <summary>Default COPY address caches. Addresses are relative to the dictionary plus the current window.</summary>
internal sealed class AddressCache
{
    private readonly long[] _near = new long[4];
    private readonly long[] _same = new long[3 * 256];
    private int _nextNear;

    internal void Reset()
    {
        Array.Clear(_near);
        Array.Clear(_same);
        _nextNear = 0;
    }

    internal long ReadAddress(byte mode, long here, ref SectionReader addresses)
        => ReadAddress(mode, here, ref addresses, _near, _same, ref _nextNear);

    internal static long ReadAddress(byte mode, long here, ref SectionReader addresses, scoped Span<long> near,
        scoped Span<long> same, ref int nextNear)
    {
        long address = mode switch
        {
            0 => addresses.ReadInteger(),
            1 => here - addresses.ReadInteger(),
            <= 5 => AddNear(near[mode - 2], addresses.ReadInteger()),
            _ => same[(mode - 6) * 256 + addresses.ReadByte()]
        };

        if (address < 0 || address >= here)
            throw new InvalidDataException("COPY address must precede the current write position.");

        near[nextNear] = address;
        nextNear = (nextNear + 1) % near.Length;
        same[(int)(address % same.Length)] = address;
        return address;
    }

    private static long AddNear(long previous, long distance)
    {
        if (distance > long.MaxValue - previous)
            throw new InvalidDataException("NEAR address exceeds the supported range.");
        return previous + distance;
    }
}
