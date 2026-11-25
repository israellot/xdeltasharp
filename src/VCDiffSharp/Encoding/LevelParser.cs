namespace VCDiffSharp.Encoding;

/// <summary>RUN, source, then target matching with sparse insertion and bounded lazy matching.</summary>
internal static class LevelParser
{
    internal static void Parse(scoped in LevelSourceMatcher source, ReadOnlySpan<byte> target, int alignedOffset,
        MatcherProfile profile, MemoryMode memory, Span<CopyMatch> commands, ref WindowWriter writer)
    {
        Span<byte> scratch = stackalloc byte[LevelTargetMatcher.ScratchSize(target.Length, profile, memory)];
        var history = new LevelTargetMatcher(target, profile, memory, scratch);
        var operations = new InstructionBuffer(commands);
        try
        {
            int probe = 0;
            long sourceBias = alignedOffset;
            while (probe <= target.Length - MatcherProfile.TargetSeed)
            {
                int minimum = Math.Max(4, operations.LastEnd - probe + 1);
                var match = Find(source, target, ref history, profile, probe, operations.Written, minimum,
                    sourceBias, minimum > 4);
                if (match.Length == 0)
                {
                    probe++;
                    continue;
                }

                operations.Add(match, target, ref writer);
                if (match.ReadOffset >= 0 && match.ReadOffset < source.Length)
                    sourceBias = match.ReadOffset - match.WriteOffset;
                probe = match.Length < profile.MaxLazy && match.End <= target.Length - 2 ? probe + 1 : match.End;
            }
            operations.Flush(target, ref writer, final: true);
        }
        finally
        {
            history.Dispose();
            operations.Dispose();
        }
    }

    private static CopyMatch Find(scoped in LevelSourceMatcher source, ReadOnlySpan<byte> target,
        scoped ref LevelTargetMatcher history, MatcherProfile profile, int probe, int written, int minimum,
        long sourceBias, bool lazy)
    {
        var tail = target[probe..];
        if (tail.Length >= 8 && tail[0] == tail[1] && tail[0] == tail[2] && tail[0] == tail[3])
        {
            int end = tail[4..].IndexOfAnyExcept(tail[0]);
            int length = end < 0 ? tail.Length : end + 4;
            if (length >= Math.Max(8, minimum)) return new CopyMatch(-1, probe, length);
        }

        var match = source.Find(target, probe, written, minimum, sourceBias + probe);
        if (match.Length != 0 || profile.TargetChain == 0) return match;
        match = history.FindAndInsert(probe, minimum, lazy);
        return match with { ReadOffset = source.Length + match.ReadOffset };
    }
}
