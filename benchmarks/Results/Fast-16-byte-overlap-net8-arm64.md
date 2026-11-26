# Fast with 16-byte seeds every eight or four source bytes

At eight-byte spacing, widening the seed from 8 to 16 bytes reduced median encode time on all four inputs. SQLite patches grew 21–24%. Halving the spacing of the 16-byte seeds recovered some of that size, but those SQLite patches still exceeded the production baseline by 16–18%.

## Patch size and time

Columns are seed width followed by source stride. Each cell shows patch bytes and median milliseconds per encode.

| Input | 8 every 8 | 8 every 4 | 16 every 8 | 16 every 4 |
|---|---:|---:|---:|---:|
| Moby-Dick | 29,959 B / 0.76 ms | 26,242 B / 1.20 ms | 30,441 B / 0.64 ms | 29,393 B / 1.02 ms |
| SQLite macOS 3.53.4 → 3.54.0 | 707,780 B / 17.60 ms | 667,671 B / 20.10 ms | 856,840 B / 13.85 ms | 826,705 B / 14.27 ms |
| SQLite macOS 3.54.0 → 3.54.1 | 235,906 B / 5.89 ms | 222,886 B / 6.30 ms | 291,318 B / 4.88 ms | 277,557 B / 5.28 ms |
| SQLite Linux 3.54.0 → 3.54.1 | 289,029 B / 7.38 ms | 274,191 B / 6.98 ms | 354,205 B / 6.34 ms | 334,476 B / 6.13 ms |

Among the two 16-byte-seed configurations, four-byte spacing reduced patch size by 3.4–5.6% relative to eight-byte spacing. Moby-Dick cost 58.8% more encode time. The macOS feature update cost 3.1% more and the macOS patch update cost 8.0% more. The Linux patch update measured 3.2% faster, with overlapping process timing ranges.

## Effect of widening the seed at a fixed stride

| Input | 16 vs 8 at stride 8 · patch | Encode time | 16 vs 8 at stride 4 · patch | Encode time |
|---|---:|---:|---:|---:|
| Moby-Dick | 1.6% | -15.4% | 12.0% | -15.0% |
| SQLite macOS 3.53.4 → 3.54.0 | 21.1% | -21.3% | 23.8% | -29.0% |
| SQLite macOS 3.54.0 → 3.54.1 | 23.5% | -17.1% | 24.5% | -16.3% |
| SQLite Linux 3.54.0 → 3.54.1 | 22.5% | -14.1% | 22.0% | -12.1% |

The data section of the macOS SQLite patch update grew from 133,683 bytes with the current 8/8 setting to 205,500 bytes with 16/8. It fell to 191,241 bytes with 16/4. Instruction and address sections were smaller with both 16-byte seeds. This explains why denser landmarks recovered some of the extra payload without matching the original patch size.

All inputs used 32-bit table entries. At stride eight the source-index pool supplied a 4 MiB array for either seed width. At stride four it supplied 8 MiB. The window-level prediction gate was active for every pair and configuration.

## Method

The production baseline was Fast at `7855222`. The three experimental builds used only complete source landmarks, with count `1 + (sourceLength - seedBytes) / strideBytes` for sources at least as long as the seed. The 16-byte hash reads two little-endian 64-bit words, rotates the first left by 19, XORs in the second, and uses the existing hash finalizer. This is the same fold used in the [16-byte block experiment](Fast-source-block-size-net8-arm64.md). Unmatched target probes still move one byte at a time. Matches extend forwards and backwards as usual. The eight-byte lazy-prediction check, collision-hop limit, and output framing are shared across builds.

All measurements used `MemoryMode.Default` with the afterburner off. They ran on an Apple M3 Max with .NET 8.0.25 Release. The Linux label identifies the SQLite executable. The encoder ran on Arm64 for all four pairs. Each configuration occupied each timing position once over four rounds. Each process warmed each input for at least 1.2 seconds, then measured nine batches of in-memory encodes into a preallocated writer. The tables report the median of the four process medians. Timing includes index construction, match finding, and writing the output. File I/O and verification were outside the timer.

VCDiffSharp checked reconstruction in each process and compared the allocating and writer APIs. xdelta3 3.2.2 and open-vcdiff independently reconstructed all 16 configuration/input combinations byte-for-byte. Patch hashes were identical across repeated runs for a given build/input. Warmed encoder-owned managed allocation was 0 bytes per call in all measurements. [Raw timings, patch hashes, and index sizes](Fast-16-byte-overlap-net8-arm64.csv) · [SQLite input hashes and downloads](Fast-release-binaries-net8-arm64.csv)

## Reproduce

Use an isolated checkout of `7855222` and apply the [experimental patch](https://github.com/israellot/xdeltasharp/blob/f405b3ea3c0ddcd2e2dbb1e57cd634dd07c88693/benchmarks/Experiments/FastSourceBlockSize/seed16-spacing.patch). The example below selects 16-byte seeds at 4-byte intervals. Set `FastStrideBytes=8` to test 16-byte seeds every 8 bytes. Read the Fast/Default-memory/writer result.

```sh
git apply --unidiff-zero /path/to/seed16-spacing.patch
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks \
  -p:FastSeedBytes=16 -p:FastStrideBytes=4 \
  -- --fast-only source.bin target.bin
```
