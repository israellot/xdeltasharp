using System.Numerics;

namespace XDeltaSharp.Format;

/// <summary>VCDIFF unsigned base-128 integers, most significant digit first.</summary>
internal static class WireInteger
{
    internal const byte ContinuationBit = 0x80;
    private const byte DigitMask = 0x7F;
    private const int BitsPerDigit = 7;

    internal static bool TryAppendDigit(ref long value, byte digit)
    {
        if (value > long.MaxValue >> BitsPerDigit)
            return false;

        value = (value << BitsPerDigit) | (uint)(digit & DigitMask);
        return true;
    }

    internal static int Size(int value) => BitOperations.Log2((uint)value) / BitsPerDigit + 1;

    internal static int Write(Span<byte> bytes, int value)
    {
        int size = Size(value);
        for (int i = size - 1; i >= 0; i--)
        {
            bytes[i] = (byte)((value & DigitMask) | (i == size - 1 ? 0 : ContinuationBit));
            value >>= BitsPerDigit;
        }
        return size;
    }
}
