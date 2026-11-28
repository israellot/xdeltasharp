# Experiments that did not ship

Each entry records what was tried and why it was dropped. The full write-ups, raw CSVs and prototype patches
are in the tree at [`05c751b`](https://github.com/israellot/xdeltasharp/tree/05c751b5be3097b7a0cf3bfacc0460ac715d51db/benchmarks/Results).
All timings are from an Apple M3 Max on .NET 8.0.25, Release.

## The Fast and Default matchers

Before the encoder took xdelta3's numbered levels it had three modes: Fast (source matches only, 8-byte
landmarks every 8 bytes, up to 256 collision links), Default and Exhaustive. The levels replaced all three.
The experiments below were run against Fast. None of them carried over.

| Idea | Result |
| --- | --- |
| 16- or 32-byte source blocks | 16 bytes encoded 5.6–31.6% faster, but SQLite patches grew 27.7–38.7%. 32 bytes was slower than 16 and larger again. |
| 16-byte seeds at 8- or 4-byte spacing | Faster at 8-byte spacing, with SQLite patches 21–24% larger. 4-byte spacing left them 16–18% larger. |
| 8-byte landmarks every 4 bytes | Every patch shrank. The index doubled and three of four pairs encoded slower. |
| 32-byte coarse pass, then 8-byte refinement | Moby-Dick shrank from 29,959 to 25,139 bytes. Encoding took at least 1.35× as long in every configuration, and the best SQLite patch was 0.4% smaller. |
| Cached hash of the next source word | Saved no chain visits on any of five inputs. |
| SIMD scan of hot buckets | Won only on synthetic repeated records. Slower than the first-byte mask on the SQLite pairs. |
| Stop after 16 candidates without improvement | Little faster than the first-byte mask on real files, and SQLite patches grew by 1,006–4,964 bytes. |

Two changes did ship in Fast before it was retired: selective lazy matching, which took the Moby-Dick patch
from 34,568 to 29,959 bytes, and a first-byte mask packed into the spare bits of 32-bit chain links, which
cut SQLite encode time by 15–19% with identical output.

## The level encoder

**Inlining a hand-written forward match.** Replacing `CommonPrefixLength` with an inlined vector comparison
at both extension sites cut level-3 encode time by 8.6% on Moby-Dick and 7.8% on a SQLite update, with
identical patches. Level 9 on SQLite was inconclusive, the scalar path assumes little-endian words, and only
.NET 8 on Arm64 was measured. It needs numbers from x64 and the newer runtimes before it is worth the code.
[Write-up](https://github.com/israellot/xdeltasharp/blob/05c751b5be3097b7a0cf3bfacc0460ac715d51db/benchmarks/Results/Level-inline-prefix-net8-arm64.md)

**More SIMD.** The span helpers the encoder already calls are vectorized by the runtime. Hashing two source
landmarks per vector made all six measured combinations slower, and a vectorized backward extension helped in
one of them. [Write-up](Level-SIMD-net8-arm64.md)
