namespace XDeltaSharp.Encoding;

/// <summary>The most profitable operation at the current greedy parse position.</summary>
internal struct WindowChoice
{
    internal int Savings { get; private set; }
    internal int WriteOffset { get; private set; }
    internal int Length { get; private set; }
    internal int Address { get; private set; }
    internal bool IsRun => Address == -1;

    internal void Consider(int savings, int writeOffset, int length, int address)
    {
        if (savings <= Savings) return;
        Savings = savings;
        WriteOffset = writeOffset;
        Length = length;
        Address = address;
    }
}
