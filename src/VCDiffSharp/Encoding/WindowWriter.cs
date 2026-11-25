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
        Instruction(bytes.Length <= 17 ? (byte)(1 + bytes.Length) : (byte)1, bytes.Length, bytes.Length > 17);
    }

    internal void Run(byte value, int length)
    {
        _data.Add(value);
        Instruction(0, length, true);
    }

    internal void Copy(int address, int length)
    {
        int mode = WriteAddress(address);
        bool fixedSize = length is >= 4 and <= 18;
        byte opcode = (byte)(19 + 16 * mode + (fixedSize ? length - 3 : 0));
        Instruction(opcode, length, !fixedSize);
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

    private int WriteAddress(int address)
    {
        int mode = 0;
        int value = address;
        int distance = sourceLength + _written - address;
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

        if (!_same.IsEmpty)
        {
            if (_addresses.Length == 0) _same.Clear();
            int slot = address % _same.Length;
            if (value >= 128 && _same[slot] == address)
            {
                mode = 6 + slot / 256;
                value = slot % 256;
            }
            _same[slot] = address;
        }

        if (mode >= 6) _addresses.Add((byte)value);
        else _addresses.Integer(value);
        _near[_nextNear] = address;
        _nextNear = (_nextNear + 1) & 3;
        return mode;
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
