using XDeltaSharp.Decoding;
using XDeltaSharp.Format;
using Xunit;

namespace XDeltaSharp.Tests;

// Adapted from TestSmallStride, TestCopyWindow and TestCopyFromEnd in xdelta3 3.2.2:
// https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/testing/regtest.cc#L774-L912
public class UpstreamEncoderTests
{
    [Theory]
    [InlineData(512, 3)]
    [InlineData(512, 9)]
    [InlineData(32768, 3)]
    [InlineData(32768, 9)]
    public void PeriodicEditsRetainSourceMatches(int size, int level)
    {
        byte[] source = new byte[size];
        new Random(714).NextBytes(source);
        for (int stride = 15; stride <= 100; stride++)
        {
            byte[] target = (byte[])source.Clone();
            for (int offset = stride; offset < size; offset += stride)
                target[offset] ^= 0xff;

            var stats = EncodeAndInspect(source, target, level);
            int edits = (size - 1) / stride;
            // Allow 10% extra literals and a partial final seed at EOF.
            int allowance = edits + edits / 10 + 8;
            Assert.True(stats.Added <= allowance && stats.SourceCopied >= size - allowance,
                $"Level {level}, size {size}, stride {stride}: {stats.Added} ADD bytes, {stats.SourceCopied} source COPY bytes.");
        }
    }

    // Per-seed ADD budgets allow some missed 16-byte matches. The level-3 index is
    // sparser than level 9; replacing 2012 bytes in the EOF case also makes direction matter.
    [Theory]
    [InlineData(false, 3, 896, 896)]
    [InlineData(false, 9, 64, 64)]
    [InlineData(true, 3, 2256, 2340)]
    [InlineData(true, 9, 1980, 2050)]
    public void ReusesSwappedBlocksInBothDirections(bool copyFromEnd, int level, int forwardBudget, int reverseBudget)
    {
        for (int seed = 1; seed <= 16; seed++)
        {
            byte[] source = new byte[4096];
            new Random(seed).NextBytes(source);
            byte[] target = (byte[])source.Clone();
            int swapLength = copyFromEnd ? 2048 : source.Length;
            for (int offset = 0; offset < swapLength; offset += 32)
            {
                source.AsSpan(offset + 16, 16).CopyTo(target.AsSpan(offset));
                source.AsSpan(offset, 16).CopyTo(target.AsSpan(offset + 16));
            }
            if (copyFromEnd)
            {
                for (int i = 2048; i < 4060; i++)
                    target[i] ^= 0xff;
                source.AsSpan(^28).CopyTo(target.AsSpan(3000));
                source.AsSpan(^30).CopyTo(target.AsSpan(3100));
                source.AsSpan(^32).CopyTo(target.AsSpan(3200));
            }
            var forward = EncodeAndInspect(source, target, level);
            var reverse = EncodeAndInspect(target, source, level);
            Assert.InRange(forward.Added, 0, forwardBudget);
            Assert.InRange(reverse.Added, 0, reverseBudget);
            // The three overlapping suffixes can also be reused via target COPY.
            int targetCopyAllowance = copyFromEnd ? 28 + 30 + 32 : 0;
            Assert.True(forward.SourceCopied >= target.Length - forwardBudget - targetCopyAllowance);
            Assert.True(reverse.SourceCopied >= source.Length - reverseBudget - targetCopyAllowance);
        }
    }

    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(32)]
    public void CanCopyTheEntireSuffixEndingAtEof(int length)
    {
        byte[] source = new byte[4096];
        new Random(4068).NextBytes(source);
        byte[] suffix = source[^length..];
        foreach (int level in new[] { 3, 9 })
        {
            Assert.Equal((0, length), EncodeAndInspect(source, suffix, level));
            Assert.Equal(length, EncodeAndInspect(suffix, source, level).SourceCopied);
        }
    }

    // Count decoded instruction sizes: data-section length also includes RUN operands,
    // and a COPY from the current target does not demonstrate reuse of the source.
    private static (int Added, int SourceCopied) EncodeAndInspect(byte[] source, byte[] target, int level)
    {
        byte[] patch = Vcdiff.Encode(source, target, new VcdiffEncodeOptions { CompressionLevel = level });
        Assert.Equal(target, Vcdiff.Decode(source, patch));
        var reader = new WindowReader(patch, Vcdiff.MaxTargetWindowBytes);
        Assert.True(reader.TryReadWindow(source.Length, 0, target.Length, out var window));
        var instructions = new SectionReader(window.Instructions);
        var addresses = new SectionReader(window.Addresses);
        var cache = new AddressCache();
        long dictionarySize = window.SegmentLength;
        int added = 0, sourceCopied = 0, written = 0;
        while (!instructions.IsEmpty)
        {
            var (first, second) = DefaultCodeTable.Lookup(instructions.ReadByte());
            Count(first, ref instructions, ref addresses);
            if (second.Kind != InstructionKind.NoOp)
                Count(second, ref instructions, ref addresses);
        }
        Assert.True(addresses.IsEmpty);
        Assert.Equal(target.Length, written);
        return (added, sourceCopied);

        // The readers are ref structs, which a local function cannot capture.
        void Count(Instruction instruction, ref SectionReader sizes, ref SectionReader copyAddresses)
        {
            int size = instruction.Size == 0 ? sizes.ReadLength() : instruction.Size;
            if (instruction.Kind == InstructionKind.Add)
                added += size;
            if (instruction.Kind == InstructionKind.Copy &&
                cache.ReadAddress(instruction.Mode, dictionarySize + written, ref copyAddresses) < dictionarySize)
                sourceCopied += size;
            written += size;
        }
    }
}
