using XDeltaSharp.Format;
#if NET9_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace XDeltaSharp.Decoding;

internal enum SectionKind : byte { Data = 1, Instructions = 2, Addresses = 4 }

internal interface ISecondaryDecoder : IDisposable
{
    ReadOnlySpan<byte> Decode(SectionKind kind, ReadOnlySpan<byte> encoded);
}

internal readonly struct SecondaryDecoderFactory(Func<int, int, ISecondaryDecoder>? createWithLimits,
    int maxExpandedSectionBytes, int maxDictionaryBytes)
{
    private readonly Func<ISecondaryDecoder>? _create;

    private SecondaryDecoderFactory(Func<ISecondaryDecoder> create) : this(null, 0, 0) => _create = create;

    internal static SecondaryDecoderFactory? From(Func<ISecondaryDecoder>? create) =>
        create is null ? null : new SecondaryDecoderFactory(create);

    internal ISecondaryDecoder Create() => _create is null ? createWithLimits!(maxExpandedSectionBytes, maxDictionaryBytes) : _create();
}

internal ref struct WindowDecoder
{
    private SectionReader _data;
    private SectionReader _instructions;
    private SectionReader _addresses;
    private readonly Span<byte> _target;
    private WindowDictionary _dictionary;
    private readonly uint? _checksum;
    private int _written;

    internal WindowDecoder(in Window window, in WindowDictionary dictionary, Span<byte> target,
        SecondaryDecoderFactory? secondaryFactory, scoped ref ISecondaryDecoder? secondary)
    {
        _data = new SectionReader(window.Data);
        _instructions = new SectionReader(window.Instructions);
        _addresses = new SectionReader(window.Addresses);
        _target = target;
        _dictionary = dictionary;
        _checksum = window.Checksum;
        if (window.CompressedSections != 0)
            ExpandSections(window, secondary ??= secondaryFactory!.Value.Create());
    }

    private void ExpandSections(in Window window, ISecondaryDecoder decoder)
    {
        if ((window.CompressedSections & (byte)SectionKind.Instructions) != 0)
            _instructions = new SectionReader(decoder.Decode(SectionKind.Instructions, window.Instructions));
        if ((window.CompressedSections & (byte)SectionKind.Data) != 0)
        {
            // ADD and nonempty RUN consume at most one data byte per target byte.
            // An empty RUN consumes one extra data byte and at least two instruction bytes.
            int maxDataLength = _target.Length + _instructions.Remaining.Length / 2;
            // xdelta3 prefixes a compressed section with its expanded size.
            if (new SectionReader(window.Data).ReadLength() > maxDataLength)
                throw new InvalidDataException("Expanded xdelta3 data section exceeds the bytes its instructions can consume.");
            _data = new SectionReader(decoder.Decode(SectionKind.Data, window.Data));
        }
        if ((window.CompressedSections & (byte)SectionKind.Addresses) != 0)
            _addresses = new SectionReader(decoder.Decode(SectionKind.Addresses, window.Addresses));
    }

    internal void Decode(ref AddressCache? cache)
    {
        if (!_addresses.IsEmpty)
            cache?.Reset();

        while (!_instructions.IsEmpty)
        {
            var (first, second) = DefaultCodeTable.Lookup(_instructions.ReadByte());
            Execute(first, ref cache);
            if (second.Kind != InstructionKind.NoOp)
                Execute(second, ref cache);
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
            var chunk = bytes[..count];
#if NET9_0_OR_GREATER
            // Sixteen serial Adler steps add 16*a plus the bytes weighted from 16 down to 1.
            if (Vector128.IsHardwareAccelerated && chunk.Length >= 16)
            {
                var highWeights = Vector128.CreateSequence((ushort)16, ushort.MaxValue);
                var lowWeights = Vector128.CreateSequence((ushort)8, ushort.MaxValue);
                do
                {
                    var block = Vector128.Create(chunk);
                    var low = Vector128.WidenLower(block);
                    var high = Vector128.WidenUpper(block);
                    b += 16 * a + Vector128.Dot(low, highWeights) + Vector128.Dot(high, lowWeights);
                    a += Vector128.Sum(low + high);
                    chunk = chunk[16..];
                } while (chunk.Length >= 16);
            }
#endif
            foreach (byte value in chunk)
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

    private void Execute(Instruction instruction, ref AddressCache? cache)
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
                cache ??= new AddressCache();
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
