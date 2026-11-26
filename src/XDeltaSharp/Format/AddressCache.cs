using XDeltaSharp.Decoding;

namespace XDeltaSharp.Format;

/// <summary>Default COPY address caches. Addresses are relative to the dictionary plus the current window.</summary>
internal sealed class AddressCache
{
    internal const int NearSize = 4;
    internal const int FirstNearMode = 2;
    internal const int FirstSameMode = FirstNearMode + NearSize;
    internal const int ModeCount = FirstSameMode + 3;
    internal const int SamePageSize = 256;
    internal const int SameSize = (ModeCount - FirstSameMode) * SamePageSize;

    private readonly long[] _near = new long[NearSize];
    private readonly long[] _same = new long[SameSize];
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
            < FirstSameMode => AddNear(near[mode - FirstNearMode], addresses.ReadInteger()),
            _ => same[(mode - FirstSameMode) * SamePageSize + addresses.ReadByte()]
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
