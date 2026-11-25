# Moby-Dick encoder profile

On the repository’s Moby-Dick file pair, the optimized Default encoder took **12.56–12.65 ms** and emitted the same 20,737 bytes as commit `ab0c875`. The largest saving came from avoiding address-mode selection for target matches that could not beat the current candidate.

| Encoder | Median time per encode | Patch bytes |
| --- | ---: | ---: |
| VCDiffSharp Default at `ab0c875` | 18.38–20.45 ms | 20,737 |
| Optimized Default | 12.56–12.65 ms | 20,737 |
| xdelta3 3.2.2, level 3, baseline wire format | 1.85 ms | 21,534 |

Apple M3 Max, Arm64, .NET 8.0.25 Release, `MemoryMode.Default`. The source was DeltaSharp’s `pg15.txt` (1,253,951 bytes) and the target was `pg2701.txt` (1,253,974 bytes). Both inputs fit in a single 8 MiB target window. For each managed build, the harness loaded the inputs once, reused the output buffer, warmed up for 1.8 seconds, and timed nine batches of 12 encodes. Two runs per build were interleaved in before/after/after/before order. Timings exclude reading files, starting processes and verifying output. The xdelta3 measurement used the native in-memory stream API, a fresh codec per encode, and a reusable output buffer. Its patch matched the xdelta3 CLI output with `-3 -W 8388608 -S none -a -n -A`. Both encoders passed byte-for-byte reconstruction.

## Where the time went

In a separate baseline EventPipe sampled-thread-time trace, `TargetMatchFinder.Search` accounted for **79.2% inclusive** of sampled time. Address selection accounted for **23.0% exclusive**, while `CommonPrefixLength` accounted for 3.4% exclusive. The address-selection samples overlap the inclusive search figure. After the change, address selection took 6.7% exclusive and target search still accounted for 77.2% inclusive of a shorter encode.

Separate Release builds instrumented the phases over 60 warmed encodes. Timers bracket groups of operations and add overhead, so these figures provide attribution rather than end-to-end timings.

| Timed region | Before | After |
| --- | ---: | ---: |
| Source index construction | 0.34 ms | 0.33 ms |
| Source probes, including lookahead | 3.11 ms | 2.33 ms |
| Inserting earlier target positions | 5.38 ms | 4.70 ms |
| Remainder of target search | 12.51 ms | 6.16 ms |

The insertion loop and number of insertions are identical in both builds. Its timing variation should not be credited to a change in index construction.

| Per-encode work counters | Before | After | xdelta3 -3 |
| --- | ---: | ---: | ---: |
| Target positions inserted | 1,235,342 | 1,235,342 | 32,576 |
| Target candidate positions examined | 940,689 | 611,841 | 48,182 |
| Target candidates priced with actual address modes | 838,023 | 70,814 | — |
| Source candidates priced with actual address modes | 58,163 | 39,152 | — |

The counts came from untimed builds. The managed candidate count includes unsuccessful seed comparisons. The native count includes each candidate visited inside `xd3_smatch`. The native instrumented patch also matched the official CLI byte-for-byte. A three-second macOS sampling profile placed `xd3_string_match_fast`, `xd3_source_extend_match` and `xd3_srcwin_move_point` at the top of the native leaf stacks.

## What xdelta3 does differently

At level 3, xdelta3 uses a nine-byte source seed, an eight-byte source step, and a four-byte target seed. Its fast configuration sets the ordinary target chain budget to four and the lazy-search chain budget to one. It can stop at a 35-byte match. The matcher raises its minimum match length during lookahead and jumps past accepted matches. Target hashes are inserted at positions that reach the small-match path. VCDiffSharp indexes skipped positions too, so subsequent instructions can still copy from those bytes. [xdelta3 matcher configuration](https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-cfgs.h) · [Minimum lengths, chain search, and target insertion](https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3.c#L3864-L4410)

Default now raises its own minimum using VCDIFF wire cost. It asks how long a COPY would have to be to beat the current choice if it had a one-byte address and the best possible opcode pairing. It checks required bytes before extending or pricing the candidate. For a sufficiently long minimum, it can use a second mandatory four-byte seed in the existing index. Recent overlapping starts are checked directly when the second seed would fall beyond the indexed history. The original candidate order is retained for ties. The source search also applies a cheaper length bound before choosing an address mode.

Two isolated diagnostic builds tested changes resembling the native search policy. Limiting a target lookup to four candidates took 7.8 ms and produced **21,419 bytes**. Inserting only the last eight positions across a skipped range took 4.2 ms and produced **21,439 bytes**. These variants each changed the baseline policy independently. The retained implementation uses the existing target index and its original memory budget.

## Validation and remaining work

The before/after Moby-Dick SHA-256 was `c5353ac489c29533ee92fce6d40be7abe70d7c7c31a6923a43dd27fe9c8100b2`. The patches from 1,200 deterministic mixed-source/target inputs also matched byte-for-byte under each of the two memory modes. Regression tests compare pruned search against exhaustive scoring and exercise paired instructions and variable-length size boundaries.

The speedup is workload-dependent. Warmed side measurements on unrelated 256 KiB data took 9.97 ms before and 10.42 ms after. A 1 MiB interrupted repeating pattern took 1.73 ms before and 1.63 ms after. Both retained identical patches. These results are from one Arm64 machine.

The remaining opportunity is target-history indexing and traversal. Insertion alone still costs roughly 5 ms on this input. A packed postings-list prototype took 13.9 ms overall, and separate oldest/newest bucket heads gave only a modest additional gain in an isolated trial while increasing index storage. A future index-layout experiment should measure insertion separately and include unrelated data and short-lived windows.

## Reproduce the managed profile

Set `books` to the DeltaSharp benchmark data directory. The focused workload warms the writer and then runs 32 batches of 12 encodes, printing the patch length, hash, and median time. Run the benchmark without a profiler for timings.

```sh
dotnet build benchmarks/VCDiffSharp.Benchmarks -c Release
bench=benchmarks/VCDiffSharp.Benchmarks/bin/Release/net8.0/VCDiffSharp.Benchmarks.dll
dotnet "$bench" --profile-default "$books/pg15.txt" "$books/pg2701.txt"
dotnet-trace collect --profile dotnet-sampled-thread-time,dotnet-common -o default.nettrace -- dotnet "$bench" --profile-default "$books/pg15.txt" "$books/pg2701.txt"
dotnet-trace report default.nettrace topN -n 15
```
