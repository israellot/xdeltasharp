using XDeltaSharp.Format;

namespace XDeltaSharp.Decoding;

internal enum SectionKind : byte { Data = 1, Instructions = 2, Addresses = 4 }

internal delegate ReadOnlySpan<byte> DecodeSecondarySection(SectionKind kind, ReadOnlySpan<byte> encoded);

internal ref struct WindowDecoder(Window window, WindowDictionary dictionary, Span<byte> target, AddressCache cache,
    DecodeSecondarySection? secondary)
{
    private SectionReader _data = new((window.CompressedSections & (byte)SectionKind.Data) == 0
        ? window.Data : secondary!(SectionKind.Data, window.Data));
    private SectionReader _instructions = new((window.CompressedSections & (byte)SectionKind.Instructions) == 0
        ? window.Instructions : secondary!(SectionKind.Instructions, window.Instructions));
    private SectionReader _addresses = new((window.CompressedSections & (byte)SectionKind.Addresses) == 0
        ? window.Addresses : secondary!(SectionKind.Addresses, window.Addresses));
    private readonly Span<byte> _target = target;
    private readonly WindowDictionary _dictionary = dictionary;
    private readonly uint? _checksum = window.Checksum;
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
        if (_checksum is uint expected && Adler32(_target) != expected)
            throw new InvalidDataException("VCDIFF target window Adler-32 checksum mismatch.");
    }

    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        uint a = 1, b = 0;
        while (!bytes.IsEmpty)
        {
            // At most 5552 bytes between reductions keeps both sums within UInt32.
            int count = Math.Min(5552, bytes.Length);
            foreach (byte value in bytes[..count])
            {
                a += value;
                b += a;
            }
            a %= modulus;
            b %= modulus;
            bytes = bytes[count..];
        }
        return (b << 16) | a;
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
