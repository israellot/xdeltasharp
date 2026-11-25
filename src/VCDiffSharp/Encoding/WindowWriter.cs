using System.Buffers;
using System.Runtime.CompilerServices;
using VCDiffSharp.Format;

namespace VCDiffSharp.Encoding;

/// <summary>Writes baseline VCDIFF operations in the address space of a preselected dictionary.</summary>
internal ref struct WindowWriter(int sourceOffset, int sourceLength, Span<byte> data, Span<byte> instructions,
    Span<byte> addresses, Span<int> sameCache)
{
    [InlineArray(4)]
    private struct NearCache { private int _first; }

    private SectionBuffer _data = new(data);
    private SectionBuffer _instructions = new(instructions);
    private SectionBuffer _addresses = new(addresses);
    private readonly Span<int> _same = sameCache;
    private ReadOnlySpan<byte> _firstLiteral;
    private NearCache _near;
    private int _nextNear;
    private int _written;
    private bool _canPair;
    private byte _lastOpcode;

    internal void Add(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        if (_data.Length == 0 && _firstLiteral.IsEmpty)
            _firstLiteral = bytes;
        else
            _data.Append(bytes);
        Instruction(AddOpcode(bytes.Length), bytes.Length, bytes.Length > 17);
    }

    internal void Run(byte value, int length)
    {
        _data.Add(value);
        Instruction(0, length, true);
    }

    internal void Copy(int address, int length)
    {
        int mode = SelectAddress(address, _written, out int value);
        CommitAddress(address, mode, value);
        bool fixedSize = length is >= 4 and <= 18;
        Instruction(CopyOpcode(mode, length), length, !fixedSize);
    }

    private static byte AddOpcode(int length) => length <= 17 ? (byte)(1 + length) : (byte)1;
    private static byte CopyOpcode(int mode, int length)
        => (byte)(19 + 16 * mode + (length is >= 4 and <= 18 ? length - 3 : 0));

    private readonly int LiteralCost(int length)
    {
        if (length == 0) return 0;
        if (length > 17) return length + 1 + WireInteger.Size(length);
        return _canPair && DefaultCodeTable.TryPair(_lastOpcode, AddOpcode(length), out _) ? length : length + 1;
    }

    internal readonly int RunSavings(int writeOffset, int length)
    {
        int pending = writeOffset - _written;
        return LiteralCost(pending + length) - LiteralCost(pending) - 2 - WireInteger.Size(length);
    }

    internal readonly int CopySavings(int writeOffset, int address, int length)
    {
        int mode = SelectAddress(address, writeOffset, out int value);
        return CopySavingsForMode(writeOffset, length, mode, mode >= 6 ? 1 : WireInteger.Size(value));
    }

    // Even eliminating a five-byte ADD header still costs at least one address byte.
    internal static int MinimumImprovingLength(int savings) => Math.Max(4, savings - 3);

    // A one-byte SELF operand offers the lowest address cost and all possible opcode pairs.
    // Its savings never decrease with length: an extra size byte costs at most one extra literal.
    internal readonly int MinimumTargetCopyLength(int writeOffset, int savings, int available)
    {
        int length = MinimumImprovingLength(savings);
        while (length <= available && CopySavingsForMode(writeOffset, length, 0, 1) <= savings)
            length++;
        return length;
    }

    private readonly int CopySavingsForMode(int writeOffset, int length, int mode, int addressBytes)
    {
        int pending = writeOffset - _written;
        int cost = 1 + addressBytes;
        if (length is < 4 or > 18)
            cost += WireInteger.Size(length);
        else
        {
            // A pending ADD already paired with the preceding COPY cannot pair a second time.
            bool canPair = pending == 0 ? _canPair : pending <= 4 && LiteralCost(pending) > pending;
            if (canPair && DefaultCodeTable.TryPair(pending == 0 ? _lastOpcode : AddOpcode(pending),
                CopyOpcode(mode, length), out _)) cost--;
        }
        if (_addresses.Length == 0 && sourceLength != 0)
            cost += WireInteger.Size(sourceOffset) + WireInteger.Size(sourceLength);
        return LiteralCost(pending + length) - LiteralCost(pending) - cost;
    }

    internal readonly int CopySavingsUpperBound(int windowLength, int earliestWriteOffset)
    {
        int remaining = windowLength - earliestWriteOffset;
        // For long matches even the cheapest complete COPY needs opcode, size, and one address byte.
        // Shortening across a size-varint boundary saves at most the number of literal bytes forfeited.
        int minCost = remaining >= 32 ? 2 + WireInteger.Size(remaining) : 1;
        if (_addresses.Length == 0 && sourceLength != 0) minCost += WireInteger.Size(sourceOffset) + WireInteger.Size(sourceLength);
        return LiteralCost(windowLength - _written) - LiteralCost(earliestWriteOffset - _written) - minCost;
    }

    private void Instruction(byte opcode, int size, bool explicitSize)
    {
        _written += size;
        if (_canPair && !explicitSize && DefaultCodeTable.TryPair(_lastOpcode, opcode, out byte pair))
        {
            _instructions.ReplaceLast(pair);
            _canPair = false;
            return;
        }
        _instructions.Add(opcode);
        if (explicitSize) _instructions.Integer(size);
        _lastOpcode = opcode;
        _canPair = !explicitSize;
    }

    private readonly int SelectAddress(int address, int writeOffset, out int value)
    {
        int mode = 0;
        value = address;
        int distance = sourceLength + writeOffset - address;
        if (distance < value)
        {
            value = distance;
            mode = 1;
        }
        for (int i = 0; i < 4; i++)
        {
            distance = address - _near[i];
            if (distance >= 0 && distance < value)
            {
                value = distance;
                mode = 2 + i;
            }
        }

        if (!_same.IsEmpty && _addresses.Length != 0 && value >= 128)
        {
            int slot = address % _same.Length;
            if (_same[slot] == address)
            {
                mode = 6 + slot / 256;
                value = slot % 256;
            }
        }
        return mode;
    }

    private void CommitAddress(int address, int mode, int value)
    {
        if (!_same.IsEmpty)
        {
            if (_addresses.Length == 0) _same.Clear();
            _same[address % _same.Length] = address;
        }
        if (mode >= 6) _addresses.Add((byte)value);
        else _addresses.Integer(value);
        _near[_nextNear] = address;
        _nextNear = (_nextNear + 1) & 3;
    }

    // Return the exact array for a single-window allocating call, or append directly to the sink.
    internal byte[]? Finish(ReadOnlySpan<byte> target, bool firstWindow, IBufferWriter<byte>? sink)
    {
        bool dictionary = sourceLength != 0 && _addresses.Length != 0;
        int literalOpcodeSize = target.Length <= 17 ? 1 : 1 + WireInteger.Size(target.Length);
        int dataLength = _firstLiteral.Length + _data.Length;
        int deltaSize = DeltaSize(target.Length, dataLength, _instructions.Length, _addresses.Length);
        int length = 1 + WireInteger.Size(deltaSize) + deltaSize;
        if (dictionary) length += WireInteger.Size(sourceOffset) + WireInteger.Size(sourceLength);

        int rawDelta = DeltaSize(target.Length, target.Length, literalOpcodeSize, 0);
        int rawLength = 1 + WireInteger.Size(rawDelta) + rawDelta;
        bool literalOnly = !target.IsEmpty && rawLength <= length;
        if (literalOnly)
        {
            dictionary = false;
            length = rawLength;
            deltaSize = rawDelta;
        }
        if (firstWindow) length += 5;
        byte[]? result = sink == null ? new byte[length] : null;
        Span<byte> output = result != null ? result : sink!.GetSpan(length);
        if (firstWindow)
        {
            FileHeader.CopyTo(output);
            output = output[5..];
        }
        output[0] = dictionary ? (byte)1 : (byte)0;
        output = output[1..];
        if (dictionary)
        {
            PutInteger(ref output, sourceLength);
            PutInteger(ref output, sourceOffset);
        }
        PutInteger(ref output, deltaSize);
        PutInteger(ref output, target.Length);
        output[0] = 0;
        output = output[1..];
        PutInteger(ref output, literalOnly ? target.Length : dataLength);
        PutInteger(ref output, literalOnly ? literalOpcodeSize : _instructions.Length);
        PutInteger(ref output, literalOnly ? 0 : _addresses.Length);
        if (literalOnly)
        {
            Put(ref output, target);
            output[0] = target.Length <= 17 ? (byte)(1 + target.Length) : (byte)1;
            if (target.Length > 17) WireInteger.Write(output[1..], target.Length);
        }
        else
        {
            Put(ref output, _firstLiteral);
            Put(ref output, _data.Written);
            Put(ref output, _instructions.Written);
            _addresses.Written.CopyTo(output);
        }
        sink?.Advance(length);
        return result;
    }

    private static ReadOnlySpan<byte> FileHeader => [0xD6, 0xC3, 0xC4, 0, 0];

    private static int DeltaSize(int target, int data, int instructions, int addresses)
        => WireInteger.Size(target) + 1 + WireInteger.Size(data) + WireInteger.Size(instructions)
            + WireInteger.Size(addresses) + data + instructions + addresses;

    private static void PutInteger(ref Span<byte> output, int value)
        => output = output[WireInteger.Write(output, value)..];

    private static void Put(ref Span<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output);
        output = output[bytes.Length..];
    }

    internal void Dispose()
    {
        _data.Dispose();
        _instructions.Dispose();
        _addresses.Dispose();
    }
}
