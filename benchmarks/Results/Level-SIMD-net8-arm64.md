# SIMD in the numbered encoders

The .NET 8 byte-span helpers already vectorize forward match extension and long RUN scans on the Apple M3 Max. For the measured inputs the existing implementation is a good baseline.

## End-to-end encode time

Percentage change relative to the corresponding unmodified build in the same timing series. Positive values mean more time.

| Input | Level | Scalar forward loop | Vector backward loop | Two hashes per vector | Target length filter |
|---|---:|---:|---:|---:|---:|
| Moby-Dick | 0 | 71.5% | -5.4% | 27.8% | — |
| Moby-Dick | 3 | 40.1% | 1.2% | 44.9% | -1.4% |
| Moby-Dick | 9 | 31.9% | 8.1% | 28.4% | -10.9% |
| SQLite macOS 3.54.0 → 3.54.1 | 0 | 28.3% | 3.7% | 12.8% | — |
| SQLite macOS 3.54.0 → 3.54.1 | 3 | 12.0% | 12.7% | 14.6% | 10.1% |
| SQLite macOS 3.54.0 → 3.54.1 | 9 | 18.2% | 11.5% | 9.2% | 7.6% |

## What ran

- The existing source and target extensions check a four-byte seed, then call `CommonPrefixLength` on the remaining spans. The Arm64 JIT disassembly of `SpanHelpers.CommonPrefixLength` showed 128-bit loads and `cmeq` on 16 byte lanes. The helper has a scalar path for short spans. The RUN scanner uses `IndexOfAnyExcept`; its negated byte-search helper also emitted 16-lane comparisons. [Runtime implementation](https://github.com/dotnet/runtime/blob/v8.0.25/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.Byte.cs)
- The scalar forward prototype replaced only the source and target `CommonPrefixLength` calls with a byte-at-a-time loop. The timing measures the entire benefit of the tuned library implementation over that loop, including SIMD. It does not isolate the cost of vector instructions alone.
- The backward prototype first checked the preceding byte, then compared unaligned 16-byte chunks from the end, located the rightmost mismatch using a lane mask and finished a short tail with the existing scalar loop. On Moby-Dick, backward matches reached 16 bytes in 682 of 4,680 accepted source extensions at level 0, 105 of 5,113 at level 3, and 17 of 6,441 at level 9. On SQLite the corresponding counts were 98 of 31,344, 310 of 28,317, and 238 of 30,168. These counters came from a separate untimed run.
- The hash prototype computed two adjacent nine-byte source-landmark hashes with `Vector128<ulong>` and committed the two bucket writes in the original order. The disassembly showed lane insertion and scalar 64-bit multiplies. This particular vector expression increased encode time on all six combinations.
- The target length filter was a scalar control. It checked the last four bytes of the minimum qualifying prefix before extending, then raised the minimum as better candidates were found. It helped on Moby-Dick at level 9 but regressed on SQLite.

## Profiles and measurement

At levels 0, 3 and 9, respectively, the forward prefix helper accounted for 10.0%, 7.8% and 7.7% of exclusive samples on Moby-Dick. At level 3 the parser’s match-selection method accounted for 31.8% exclusive. At level 9, target-chain search and insertion accounted for 36.8% exclusive and the parser’s match-selection method for 32.6%. Inlined work is attributed to the containing method. Samples came from separate baseline runs using dotnet-trace sampled-thread-time and runtime method events.

The comparison used commit `f405b3e`, .NET 8.0.25 Release on macOS Arm64 (Apple M3 Max), the repository’s Moby-Dick file pair and the [SQLite macOS executable pair](Fast-release-binaries-net8-arm64.md). Inputs and the reusable writer were in memory. Each fresh process warmed each case for at least 1.2 seconds and timed nine batches. The table compares the average of two process medians per variant with its paired control. The forward-scalar and hash experiments shared one control series, the backward experiment and the target filter each had their own. Individual SQLite level-9 medians varied across runs. JIT listings were collected separately with ReadyToRun and tiering disabled. Every measured build produced the same patch SHA-256 per input/level and decoded to the original target bytes. These measurements cover the three representative numbered levels on Arm64. [Raw timings and patch hashes](Level-SIMD-net8-arm64.csv)
