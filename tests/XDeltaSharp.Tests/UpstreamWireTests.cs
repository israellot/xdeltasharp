using XDeltaSharp.Decoding;
using XDeltaSharp.Encoding;
using XDeltaSharp.Format;
using Xunit;
using static XDeltaSharp.Tests.WindowVectors;

namespace XDeltaSharp.Tests;

// Adapted from test_address_cache and test_encode_decode_uint{32,64}_t in xdelta3 3.2.2:
// https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-test.h#L535-L623
// https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-test.h#L795-L898
public class UpstreamWireTests
{
    [Fact]
    public void RepeatedNearbyAndDistantAddressesSurviveCacheReplacement()
    {
        var random = new Random(9173);
        var expected = new int[10_000];
        var writer = new WindowWriter(0, 0, [], [], [], new int[768]);
        try
        {
            // Seed four target bytes so the first COPY can reference earlier output.
            writer.Run(42, 4);
            for (int i = 0; i < expected.Length; i++)
            {
                int here = 4 + 4 * i;
                int previous = expected[random.Next(Math.Max(1, i))];
                expected[i] = random.Next(10) switch
                {
                    0 => expected[Math.Max(0, i - random.Next(1, 257))],
                    < 4 => Math.Min(here - 1, previous + random.Next(1, 256)),
                    _ => random.Next(here)
                };
                writer.Copy(expected[i], 4);
            }

            var inst = new SectionReader(writer.Instructions);
            Assert.Equal(0, inst.ReadByte()); // RUN opcode
            Assert.Equal(4, inst.ReadLength());
            var addr = new SectionReader(writer.Addresses);
            var cache = new AddressCache();
            var modes = new HashSet<byte>();
            for (int i = 0; i < expected.Length; i++)
            {
                var (copy, second) = DefaultCodeTable.Lookup(inst.ReadByte());
                Assert.Equal(InstructionKind.Copy, copy.Kind);
                Assert.Equal(4, copy.Size);
                Assert.Equal(InstructionKind.NoOp, second.Kind);
                modes.Add(copy.Mode);
                Assert.Equal(expected[i], cache.ReadAddress(copy.Mode, 4L + 4L * i, ref addr));
            }
            Assert.Equal(9, modes.Count);
            Assert.True(inst.IsEmpty);
            Assert.True(addr.IsEmpty);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void IntegersUseTheExpectedWidthAtEverySevenBitTransition()
    {
        var vectors = new List<(long Value, int Width)> { (0, 1), (1, 1), (2, 1) };
        for (int width = 1; width <= 8; width++)
        {
            long boundary = 1L << (7 * width);
            vectors.Add((boundary - 1, width));
            vectors.Add((boundary, width + 1));
            vectors.Add((boundary + 1, width + 1));
        }
        vectors.AddRange([(int.MaxValue - 1, 5), (int.MaxValue, 5), ((long)int.MaxValue + 1, 5),
            (long.MaxValue - 1, 9), (long.MaxValue, 9)]);

        var sequence = new List<byte>();
        Span<byte> encoded = stackalloc byte[5];
        foreach (var (value, width) in vectors)
        {
            byte[] reference = Integer(value);
            Assert.Equal(width, reference.Length);
            if (value <= int.MaxValue) // The encoder accepts span-sized inputs.
            {
                Assert.Equal(width, WireInteger.Size((int)value));
                Assert.Equal(width, WireInteger.Write(encoded, (int)value));
                Assert.True(encoded[..width].SequenceEqual(reference));
            }
            sequence.AddRange(reference);
        }
        var reader = new SectionReader(sequence.ToArray());
        foreach (var (value, width) in vectors)
        {
            int before = reader.Remaining.Length;
            Assert.Equal(value, reader.ReadInteger());
            Assert.Equal(width, before - reader.Remaining.Length);
        }
        Assert.True(reader.IsEmpty);

        // Empty segments exercise 64-bit offsets without allocating a dictionary of that size.
        byte[] patch = Patch(vectors.Select(v => Window(0, [], [], [], 1, 0, v.Value)).ToArray());
        using var input = new MemoryStream(patch);
        CheckWindowOffsets(new WindowReader(patch, 0));
        CheckWindowOffsets(new WindowReader(input, 0, Array.MaxLength));
        void CheckWindowOffsets(WindowReader windows)
        {
            foreach (var (value, _) in vectors)
            {
                Assert.True(windows.TryReadWindow(long.MaxValue, 0, 0, out var window));
                Assert.Equal(value, window.SegmentOffset);
            }
            Assert.False(windows.TryReadWindow(long.MaxValue, 0, 0, out _));
        }
    }

    [Fact]
    public void RejectsAnIntegerOnePastTheSupportedRangeInHeadersAndSections()
    {
        // 2^63 in most-significant-digit-first base 128.
        byte[] digits = [0x81, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00];
        Assert.Contains("integer exceeds", Assert.Throws<InvalidDataException>(() =>
            new SectionReader(digits).ReadInteger()).Message);

        byte[] patch = Patch([1, 0, .. digits]); // Source segment offset is the next integer.
        Assert.Contains("integer exceeds", Assert.Throws<InvalidDataException>(() => Vcdiff.Decode([], patch)).Message);
        using var input = new MemoryStream(patch);
        Assert.Contains("integer exceeds", Assert.Throws<InvalidDataException>(() =>
            Vcdiff.Decode(Stream.Null, input, Stream.Null)).Message);
    }
}
