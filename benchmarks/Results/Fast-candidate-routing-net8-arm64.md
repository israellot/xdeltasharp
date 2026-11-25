# Reducing Fast source-candidate visits

The packed tail mask skipped 22–26% of source candidates on the three SQLite updates and reduced median encode time by 15–19%. The SIMD hot-bucket prototype had its largest win on repeated synthetic records. All patches matched the baseline byte-for-byte.

| Input | Baseline visits | Packed-mask visits | SIMD survivor visits | Baseline ms | Packed-mask ms | SIMD ms |
|---|---:|---:|---:|---:|---:|---:|
| Moby-Dick | 26,146 | 22,146 | 23,082 | 0.849 | 0.804 | 0.843 |
| SQLite macOS 3.53.4 → 3.54.0 | 909,731 | 673,640 | 818,272 | 17.304 | 13.993 | 18.411 |
| SQLite macOS 3.54.0 → 3.54.1 | 266,430 | 198,868 | 235,123 | 5.744 | 4.844 | 6.040 |
| SQLite Linux 3.54.0 → 3.54.1 | 387,987 | 301,489 | 249,243 | 7.377 | 6.248 | 7.049 |
| Synthetic shared headers | 136,965 | 136,965 | 12,710 | 0.832 | 0.843 | 0.160 |

A candidate visit counts an individually inspected source position. The raw data also reports primary-chain next-link reads. Counters come from separate instrumentation. The mask comparison includes reading the bucket head. The SIMD comparison includes batch metadata work and cache construction in its timings. Individual candidate counts for SIMD exclude building that cache.

## A summary of the remaining chain

Every source candidate must match the first target byte at the probe. The maximum source segment contains 8,388,608 eight-byte landmarks, so its one-based landmark numbers fit in the low 24 bits of a 32-bit entry. Assign each source first byte to one of eight classes with `firstByte & 7`. During reverse insertion, pack the union of the classes in the remaining chain into the upper byte of each head and link. At lookup, one bit test determines whether to stop. A shared class may admit extra work. An absent class rules out every remaining candidate, even when backward extension is possible.

The original 8- and 16-bit layouts keep using their ordinary chains. No additional index array is needed. Numbers of full extensions remained identical because the existing matcher already checked first-byte equality after loading each position. The speedup on SQLite comes with fewer dependent link reads and source-position inspections. Moby-Dick timing ranges overlapped (0.79–0.92 ms baseline, 0.79–0.91 ms with masks). The small synthetic table does not have spare high bits and gained nothing. [Packed-mask prototype](../Experiments/FastCandidateRouting/bitmap.patch)

## Routing heavily reused buckets

Eight buckets accounted for 31% of all visits on the macOS feature update, 32% on the macOS patch update and 52% on Linux. The SIMD prototype waits until two searches reach the 17th entry in the same bucket. It caches the remaining original positions up to the 256-hop limit, preserving order and the exact candidate budget. A direct-mapped cache holds up to 32 such tails.

It compares 16 candidate descriptors at once. Each contains bytes at offsets 0, +4, +8, −1 and −2. The first byte must match. For example, with an incumbent of eight bytes, a mismatch at +8 permits at most eight forward bytes. To improve, at least the preceding byte must then match. Candidates failing both conditions can be dropped as a group. The remaining positions undergo the ordinary byte comparisons. This also handles hash collisions, limited pending literals and source/window edges.

On Linux SQLite the routed path examined 249,243 individual positions, compared 13,009 SIMD batches, and copied descriptors for 14,837 positions during cache construction. On the macOS feature update it constructed 69,667 descriptors while avoiding 91,459 individual visits. Reduced visits on that workload came with longer encode time. Waiting for 32 or 64 hops before activating the cache also failed to produce a consistent real-file win. The most promising refinement is admission based on both repeated use and measured rejection rate. [SIMD prototype](../Experiments/FastCandidateRouting/routed.patch)

Two other prototypes were less useful. An exact first-byte tag for homogeneous tails lost time on construction and mixed chains. A small hot-query memo cache reused an earlier result only when the surrounding target bytes proved all candidate lengths and tie decisions identical. On the macOS patch update it hit 57 times in 2,875 eligible lookups. Both emitted the same patches in these trials.

## Method

Apple M3 Max, .NET 8.0.25 Release, Fast, default memory policy and no afterburner. Baseline commit `4196024`. Each timed build ran in three fresh processes with rotated order. Each input was warmed for at least 1.2 seconds and timed in nine batches, in-memory, with a preallocated writer. The table reports medians across the three process medians. Index construction, training and cache disposal are included. The small shared-header workload is the one used in the [adjacent hash experiment](Fast-adjacent-hash-net8-arm64.md).

VCDiffSharp and xdelta3 reconstructed all five targets. The two main prototypes produced byte-identical output to baseline on the five benchmark inputs and on 480 additional deterministic input/memory combinations spanning small, medium and large source indexes. Production Fast was left at the baseline algorithm. [Per-run measurements](Fast-candidate-routing-net8-arm64.csv)

To reproduce, apply either prototype patch in isolation to the baseline checkout with `git apply --unidiff-zero`, then run `dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks -- --fast-only source.bin target.bin`.
