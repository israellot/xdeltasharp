# DeltaSharp’s inline forward matcher in the numbered encoders

On the Apple M3 Max, using DeltaSharp’s `MatchForward` for both source and target extension reduced median level-3 encode time by 8.6% on Moby-Dick and 7.8% on the macOS SQLite patch update. These are whole-encode measurements on .NET 8.0.25 with the standard four-byte seed check intact.

## Results

Times in milliseconds. Each row compares the production implementation with replacing the source call, target call, or both. Each cell is the median of four process medians. Level 0 skips target search, so the target-only column at level 0 is a timing-noise control.

| Input | Level | Runtime helper | Source only | Target only | Both | Time change, both |
|---|---:|---:|---:|---:|---:|---:|
| Moby-Dick | 0 | 0.735 | 0.697 | 0.770 | 0.689 | -6.3% |
| Moby-Dick | 3 | 1.297 | 1.272 | 1.259 | 1.185 | -8.6% |
| Moby-Dick | 9 | 6.201 | 6.253 | 6.136 | 5.918 | -4.6% |
| SQLite | 0 | 2.557 | 2.398 | 2.528 | 2.337 | -8.6% |
| SQLite | 3 | 7.390 | 7.373 | 7.032 | 6.813 | -7.8% |
| SQLite | 9 | 44.136 | 44.510 | 38.825 | 43.535 | -1.4% |

SQLite level 9 is inconclusive for the two-site change. The four baseline medians ranged from 41.14 to 44.92 ms and the two-site build ranged from 37.28 to 55.54 ms. The target-only build had the lowest median on this case (38.83 ms) but also had one 48.36 ms run.

## What was inlined

Warmed Tier 1 JIT listings at level 9 showed the framework call on the baseline extension path. In the candidate, both `LevelSourceMatcher.Extend` and `LevelTargetMatcher.FindAndInsert` contained the actual first-vector comparison. The following instructions appeared inside the source matcher:

```asm
ldr     q16, [x2]
ldr     q17, [x0]
cmeq    v16.16b, v16.16b, v17.16b
shrn    v16.8b, v16.8h, #4
umov    x3, v16.d[0]
cmn     x3, #1
beq     G_M000_IG17
```

A first-vector mismatch takes the local mask-to-prefix path (invert, reverse bits, count leading zeros, divide by four). A match covering more than the first vector can call the tail helper. Short comparable spans use the scalar helper. Separate untimed counters recorded **31,182 of 33,528** prefix calls finishing on the first-vector mismatch at level 3 on Moby-Dick and **167,542 of 184,434** on SQLite. The comparable spans start after the verified four-byte seed.

A second experiment kept the custom algorithm at both sites and toggled only `AggressiveInlining` versus `NoInlining` on its entry point. On Moby-Dick at level 3 these were effectively tied (1.175 vs 1.172 ms). On SQLite at level 3 they measured 6.758 vs 7.115 ms. The main comparison measures the combined effect of inlining, mask extraction, tail handling and the JIT’s resulting caller code. It cannot assign the whole gain to function-call overhead.

## Validation and reproduction

The experiment used VCDiffSharp commit `a2f4b71` and copied the forward portion of DeltaSharp’s working-tree `src/DeltaSharp/Util/SpanHelper.cs` (source file SHA-256 `776c2d75c2b3c6e41464575a9c0d79224a0ec23b1253114d6c050d419485d183`). All four builds produced identical patch hashes for the six measured combinations and decoded the patches byte-for-byte. A separate 1,120-case sweep of levels 0–9, both memory modes, small inputs and the two real pairs produced the same aggregate patch fingerprint before and after. Direct comparison with `CommonPrefixLength` passed 209,325 length, mismatch and alignment cases with hardware intrinsics enabled and another 209,325 with them disabled on the same little-endian Arm64 machine.

Release builds loaded the inputs once and encoded into a reusable writer. Each fresh process warmed each input/level for at least 1.2 seconds and timed nine calibrated batches. Variant order rotated across four rounds. Inputs were the DeltaSharp Moby-Dick editions and SQLite macOS arm64 CLI 3.54.0 → 3.54.1. Disassembly used normal tiering and PGO in separate runs. Runtime versions and architectures other than .NET 8 Arm64 were outside the timing sweep. The copied scalar XOR path assumes little-endian word layout.

Apply the [benchmark-only patch](../Experiments/InlineMatchForward/level-forward.patch) to an isolated checkout with `git apply --unidiff-zero`. Run the [level benchmark](../VCDiffSharp.Benchmarks/Program.cs) with `--levels source.bin target.bin` to compare on another pair. [Per-process timings and patch hashes](Level-inline-prefix-net8-arm64.csv)
