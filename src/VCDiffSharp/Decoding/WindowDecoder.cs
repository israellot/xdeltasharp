using VCDiffSharp.Format;

namespace VCDiffSharp.Decoding;

internal ref struct WindowDecoder(Window window, WindowDictionary dictionary, Span<byte> target, AddressCache cache)
{
    private SectionReader _data = new(window.Data);
    private SectionReader _instructions = new(window.Instructions);
    private SectionReader _addresses = new(window.Addresses);
    private readonly Span<byte> _target = target;
    private readonly WindowDictionary _dictionary = dictionary;
    private int _written;

    internal void Decode()
    {
        if (!_addresses.IsEmpty)
            cache.Reset();

        while (!_instructions.IsEmpty)
        {
            var (first, second) = DefaultCodeTable.Lookup(_instructions.ReadByte());
            Execute(first);
            if (second.Kind != InstructionKind.NoOp)
                Execute(second);
        }

        if (_written != _target.Length || !_data.IsEmpty || !_addresses.IsEmpty)
            throw new InvalidDataException("VCDIFF instructions do not match the declared window and section lengths.");
    }

    private void Execute(Instruction instruction)
    {
        int size = instruction.Size == 0 ? _instructions.ReadLength() : instruction.Size;
        if (size > _target.Length - _written)
            throw new InvalidDataException("Instruction exceeds the declared target window.");

        var destination = _target.Slice(_written, size);
        switch (instruction.Kind)
        {
            case InstructionKind.Add:
                _data.Take(size).CopyTo(destination);
                break;
            case InstructionKind.Run:
                destination.Fill(_data.ReadByte());
                break;
            case InstructionKind.Copy:
                long address = cache.ReadAddress(instruction.Mode, _dictionary.Length + _written, ref _addresses);
                Copy(address, destination);
                break;
        }
        _written += size;
    }

    private void Copy(long address, Span<byte> destination)
    {
        if (address < _dictionary.Length)
        {
            _dictionary.Copy(address, destination);
            return;
        }

        int readOffset = (int)(address - _dictionary.Length);
        int available = _written - readOffset;
        // Each step adds bytes that may be read by the same overlapping COPY.
        while (!destination.IsEmpty)
        {
            int count = Math.Min(available, destination.Length);
            _target.Slice(readOffset, count).CopyTo(destination);
            destination = destination[count..];
            available += count;
        }
    }
}
