#if NET11_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace XDeltaSharp.Encoding;

/// <summary>A COPY or RUN proposed for the current target window.</summary>
/// <param name="ReadOffset">Read position for a COPY, or <see cref="RunOffset"/> for a RUN. Target matcher positions
/// are relative to the current window until the parser adds the source segment length.</param>
/// <param name="WriteOffset">Target-window offset where the operation begins.</param>
/// <param name="Length">Number of matching bytes.</param>
internal readonly record struct CopyMatch(int ReadOffset, int WriteOffset, int Length)
{
    internal const int RunOffset = -1;
    internal bool IsRun => ReadOffset < 0;
    internal int End => WriteOffset + Length;

    internal static int CommonSuffixLength(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int matched = 0;
#if NET11_0_OR_GREATER
        // Most probes differ at the first byte. Scan whole vectors only after that check.
        if (Vector128.IsHardwareAccelerated && limit >= 16)
        {
            if (left[^1] != right[^1]) return 0;
            do
            {
                var a = Vector128.Create(left.Slice(left.Length - matched - 16, 16));
                var b = Vector128.Create(right.Slice(right.Length - matched - 16, 16));
                if (!Vector128.EqualsAll(a, b))
                    return matched + Vector128.IndexOf(Vector128.Reverse(Vector128.Equals(a, b)), (byte)0);
                matched += 16;
            } while (limit - matched >= 16);
        }
#endif
        while (matched < limit && left[^(matched + 1)] == right[^(matched + 1)]) matched++;
        return matched;
    }
}
