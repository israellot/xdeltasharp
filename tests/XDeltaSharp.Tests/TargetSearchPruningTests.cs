using XDeltaSharp.Encoding;
using Xunit;

namespace XDeltaSharp.Tests;

public class TargetSearchPruningTests
{
    [Theory]
    [InlineData(60, 0)]
    [InlineData(256, 0)]
    [InlineData(800, 65536)]
    public void FilteredSearchMatchesExhaustiveSavings(int length, int dictionaryLength)
    {
        // Common seeds with divergent continuations, short periodic overlaps and hash collisions.
        var random = new Random(841);
        byte[] window = new byte[length];
        for (int i = 0; i < length; i++)
            window[i] = (byte)(i % 23 < 17 ? i % 3 : random.Next(4));

        Span<byte> scratch = stackalloc byte[TargetMatchFinder.SmallTableSize(length, MemoryMode.Default)];
        scoped TargetMatchFinder history = new(window, scratch, MemoryMode.Default);
        try
        {
            for (int probe = 5; probe + 4 <= length; probe += 3)
                CheckProbe(ref history, window, probe, dictionaryLength);
        }
        finally { history.Dispose(); }
    }

    private static void CheckProbe(scoped ref TargetMatchFinder history, byte[] window, int probe, int dictionaryLength)
    {
        int pending = probe % 5;
        var writer = new WindowWriter(0, dictionaryLength, new byte[32], new byte[32],
            new byte[32], new int[768]);
        try
        {
            writer.Add(window.AsSpan(0, probe - pending - 4));
            writer.Copy(0, 4); // A prior COPY can pair with a pending one-byte ADD.
            WindowChoice best = default;
            best.Consider(probe % 18, probe, 4, -1); // Existing RUN or source candidate to beat.
            int expectedSavings = best.Savings;
            var tail = window.AsSpan(probe);
            for (int earlier = 0; earlier < probe; earlier++)
            {
                int match = tail.CommonPrefixLength(window.AsSpan(earlier));
                if (match >= 4)
                    expectedSavings = Math.Max(expectedSavings, writer.CopySavings(probe, dictionaryLength + earlier, match));
            }
            history.Search(probe, dictionaryLength, in writer, ref best, writer.CopySavingsUpperBound(window.Length, probe));
            Assert.Equal(expectedSavings, best.Savings);
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void MinimumLengthIncludesPairsAndVariableLengthBoundaries()
    {
        var writer = new WindowWriter(0, 256, stackalloc byte[16], stackalloc byte[16], stackalloc byte[16], []);
        try
        {
            foreach (int prefix in new[] { 0, 1, 4 })
            {
                if (prefix != 0) writer.Add(new byte[prefix]);
                foreach (int pending in new[] { 0, 1, 4, 17 })
                {
                    int probe = (prefix == 4 ? 5 : prefix) + pending;
                    foreach (int length in new[] { 4, 5, 6, 7, 17, 18, 19, 126, 127, 128, 16383, 16384 })
                    {
                        int savings = writer.CopySavings(probe, 0, length);
                        int minimum = writer.MinimumTargetCopyLength(probe, savings, length + 4);
                        Assert.True(minimum > length);
                        if (minimum <= length + 4) Assert.True(writer.CopySavings(probe, 0, minimum) > savings);
                    }
                }
            }
        }
        finally { writer.Dispose(); }
    }
}
