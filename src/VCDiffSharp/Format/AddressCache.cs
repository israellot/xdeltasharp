using VCDiffSharp.Decoding;

namespace VCDiffSharp.Format;

/// <summary>Default COPY address caches. Addresses are relative to the dictionary plus the current window.</summary>
internal sealed class AddressCache
{
    private readonly uint[] _near = new uint[4];
    private readonly uint[] _same = new uint[3 * 256];
    private int _nextNear;

    internal void Reset()
    {
        Array.Clear(_near);
        Array.Clear(_same);
        _nextNear = 0;
    }

    internal uint ReadAddress(byte mode, long here, ref SectionReader addresses)
    {
        long address = mode switch
        {
            0 => addresses.ReadInteger(),
            1 => here - addresses.ReadInteger(),
            <= 5 => (long)_near[mode - 2] + addresses.ReadInteger(),
            _ => _same[(mode - 6) * 256 + addresses.ReadByte()]
        };

        if (address < 0 || address >= here)
            throw new InvalidDataException("COPY address must precede the current write position.");

        uint value = (uint)address;
        _near[_nextNear] = value;
        _nextNear = (_nextNear + 1) % _near.Length;
        _same[value % _same.Length] = value;
        return value;
    }
}
