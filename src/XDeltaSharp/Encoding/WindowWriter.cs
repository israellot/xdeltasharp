using System.Buffers;
using System.Runtime.CompilerServices;
using XDeltaSharp.Format;

namespace XDeltaSharp.Encoding;

/// <summary>Writes baseline VCDIFF operations in the address space of a preselected dictionary.</summary>
internal ref struct WindowWriter(int sourceOffset, int sourceLength, Span<byte> data, Span<byte> instructions,
    Span<byte> addresses, Span<int> sameCache)
{
    [InlineArray(AddressCache.NearSize)]
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
        Instruction(DefaultCodeTable.AddOpcode(bytes.Length), bytes.Length, bytes.Length > DefaultCodeTable.MaxFixedAddSize);
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
        Instruction(DefaultCodeTable.CopyOpcode(mode, length), length, !DefaultCodeTable.HasFixedCopySize(length));
    }

    private readonly int LiteralCost(int length)
    {
        if (length == 0) return 0;
        if (length > DefaultCodeTable.MaxFixedAddSize) return length + 1 + WireInteger.Size(length);
        return _canPair && DefaultCodeTable.TryPair(_lastOpcode, DefaultCodeTable.AddOpcode(length), out _) ? length : length + 1;
    }

    internal readonly int RunSavings(int writeOffset, int length)
    {
        int pending = writeOffset - _written;
        return LiteralCost(pending + length) - LiteralCost(pending) - 2 - WireInteger.Size(length);
    }

    internal readonly int CopySavings(int writeOffset, int address, int length)
    {
        int mode = SelectAddress(address, writeOffset, out int value);
        int addressBytes = mode >= AddressCache.FirstSameMode ? 1 : WireInteger.Size(value);
        int pending = writeOffset - _written;
        int cost = 1 + addressBytes;
        if (!DefaultCodeTable.HasFixedCopySize(length))
            cost += WireInteger.Size(length);
        else
        {
            // A pending ADD already paired with the preceding COPY cannot pair a second time.
            bool canPair = pending == 0 ? _canPair : pending <= 4 && LiteralCost(pending) > pending;
            if (canPair && DefaultCodeTable.TryPair(pending == 0 ? _lastOpcode : DefaultCodeTable.AddOpcode(pending),
                DefaultCodeTable.CopyOpcode(mode, length), out _)) cost--;
        }
        if (_addresses.Length == 0 && sourceLength != 0)
            cost += WireInteger.Size(sourceOffset) + WireInteger.Size(sourceLength);
        return LiteralCost(pending + length) - LiteralCost(pending) - cost;
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
        for (int i = 0; i < AddressCache.NearSize; i++)
        {
            distance = address - _near[i];
            if (distance >= 0 && distance < value)
            {
                value = distance;
                mode = AddressCache.FirstNearMode + i;
            }
        }

        if (!_same.IsEmpty && _addresses.Length != 0 && value >= 128)
        {
            int slot = address % _same.Length;
            if (_same[slot] == address)
            {
                mode = AddressCache.FirstSameMode + slot / AddressCache.SamePageSize;
                value = slot % AddressCache.SamePageSize;
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
        if (mode >= AddressCache.FirstSameMode) _addresses.Add((byte)value);
        else _addresses.Integer(value);
        _near[_nextNear] = address;
        _nextNear = (_nextNear + 1) & (AddressCache.NearSize - 1);
    }

    internal readonly ReadOnlySpan<byte> Instructions => _instructions.Written;
    internal readonly ReadOnlySpan<byte> Addresses => _addresses.Written;
    private readonly int DataLength => _firstLiteral.Length + _data.Length;

    // Without COPY addresses, an empty data buffer leaves only the borrowed first ADD, if any.
    // A lone RUN stores one data byte and has no borrowed literal, regardless of its encoded length.
    internal readonly bool IsEmptyOrSingleDataOperation => _addresses.Length == 0 &&
        (_data.Length == 0 || (_data.Length == 1 && _firstLiteral.IsEmpty));

    internal readonly record struct WindowLayout(int Length, int DeltaSize, int InstructionSize, bool UsesDictionary, bool LiteralOnly);

    // Includes the dictionary descriptor, section headers, and single-ADD fallback.
    internal readonly WindowLayout Measure(int targetLength)
    {
        bool dictionary = sourceLength != 0 && _addresses.Length != 0;
        int deltaSize = DeltaSize(targetLength, DataLength, _instructions.Length, _addresses.Length);
        int length = 1 + WireInteger.Size(deltaSize) + deltaSize;
        if (dictionary) length += WireInteger.Size(sourceOffset) + WireInteger.Size(sourceLength);

        int literalOpcodeSize = targetLength <= DefaultCodeTable.MaxFixedAddSize ? 1 : 1 + WireInteger.Size(targetLength);
        int rawDelta = DeltaSize(targetLength, targetLength, literalOpcodeSize, 0);
        int rawLength = 1 + WireInteger.Size(rawDelta) + rawDelta;
        return targetLength != 0 && rawLength <= length
            ? new WindowLayout(rawLength, rawDelta, literalOpcodeSize, UsesDictionary: false, LiteralOnly: true)
            : new WindowLayout(length, deltaSize, _instructions.Length, dictionary, LiteralOnly: false);
    }

    internal byte[]? Finish(ReadOnlySpan<byte> target, bool firstWindow, IBufferWriter<byte>? sink)
        => Finish(target, firstWindow, sink, Measure(target.Length));

    internal byte[]? Finish(ReadOnlySpan<byte> target, bool firstWindow, IBufferWriter<byte>? sink, WindowLayout layout)
    {
        int length = layout.Length + (firstWindow ? FileHeader.Default.Length : 0);
        byte[]? result = sink is null ? new byte[length] : null;
        Span<byte> output = sink is null ? result : sink.GetSpan(length);
        if (firstWindow)
        {
            FileHeader.Default.CopyTo(output);
            output = output[FileHeader.Default.Length..];
        }
        output[0] = (byte)(layout.UsesDictionary ? WindowFlags.Source : WindowFlags.None);
        output = output[1..];
        if (layout.UsesDictionary)
        {
            PutInteger(ref output, sourceLength);
            PutInteger(ref output, sourceOffset);
        }
        PutInteger(ref output, layout.DeltaSize);
        PutInteger(ref output, target.Length);
        output[0] = 0;
        output = output[1..];
        PutInteger(ref output, layout.LiteralOnly ? target.Length : DataLength);
        PutInteger(ref output, layout.InstructionSize);
        PutInteger(ref output, layout.LiteralOnly ? 0 : _addresses.Length);
        if (layout.LiteralOnly)
        {
            Put(ref output, target);
            output[0] = DefaultCodeTable.AddOpcode(target.Length);
            if (target.Length > DefaultCodeTable.MaxFixedAddSize) WireInteger.Write(output[1..], target.Length);
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
