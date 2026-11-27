namespace XDeltaSharp.Encoding;

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
            // A source COPY updates the expected source address for each subsequent target probe.
            long sourceBias = alignedOffset;
            while (probe <= target.Length - MatcherProfile.TargetSeed)
            {
                // A lazy probe must extend past the pending match to replace it.
                int minimum = Math.Max(MatcherProfile.TargetSeed, operations.LastEnd - probe + 1);
                bool lazy = minimum > MatcherProfile.TargetSeed;
                var match = Find(source, target, ref history, profile, probe, operations.Written, minimum, sourceBias, lazy);
                if (match.Length == 0)
                {
                    probe++;
                    continue;
                }

                operations.Add(match, target, ref writer);
                if (match.ReadOffset >= 0 && match.ReadOffset < source.Length)
                    sourceBias = match.ReadOffset - match.WriteOffset;

                // A short match is eligible for lookahead only when at least two more bytes follow.
                probe = match.Length < profile.LazyMatchLimit && match.End <= target.Length - 2
                    ? probe + 1 : match.End;
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
        // Four equal bytes are compared here before scanning, because most probes are not the start of a run.
        const int compared = 4;
        var tail = target[probe..];
        if (tail.Length >= MatcherProfile.MinRunLength &&
            tail[0] == tail[1] && tail[0] == tail[2] && tail[0] == tail[3])
        {
            int firstDifferent = tail[compared..].IndexOfAnyExcept(tail[0]);
            int length = firstDifferent < 0 ? tail.Length : firstDifferent + compared;
            if (length >= Math.Max(MatcherProfile.MinRunLength, minimum))
                return new CopyMatch(CopyMatch.RunOffset, probe, length);
        }

        var match = source.Find(target, probe, written, minimum, sourceBias + probe);
        if (match.Length != 0 || profile.TargetCandidates == 0)
            return match;

        match = history.FindAndInsert(probe, minimum, lazy);
        // VCDIFF target addresses follow the source segment in the COPY address space.
        return match with { ReadOffset = source.Length + match.ReadOffset };
    }
}
