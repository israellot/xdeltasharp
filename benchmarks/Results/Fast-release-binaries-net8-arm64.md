# Fast encoding on released executables

Fast's selective COPY lookahead produced smaller VCDIFF patches on all seven release updates tested. SQLite's 3.54.0 → 3.54.1 patch shrank by 5.7% for the macOS arm64 executable and 9.9% for the Linux x64 executable. Every timed encode ran on the same Apple M3 Max.

| Program and platform | Update | Patch bytes before → after | Size change | Encode ms before → after | Time change |
|---|---|---:|---:|---:|---:|
| SQLite CLI · macOS arm64 | 3.53.4 → 3.54.0 | 721,205 → 707,780 | -1.9% | 18.73 → 17.26 | -7.9% |
| SQLite CLI · macOS arm64 | 3.54.0 → 3.54.1 | 250,196 → 235,906 | -5.7% | 7.49 → 5.87 | -21.6% |
| SQLite CLI · Linux x64 | 3.54.0 → 3.54.1 | 320,844 → 289,029 | -9.9% | 9.14 → 7.88 | -13.7% |
| jq · macOS arm64 | 1.8.0 → 1.8.1 | 70,998 → 68,291 | -3.8% | 3.09 → 2.47 | -20.3% |
| jq · macOS arm64 | 1.8.1 → 1.8.2 | 140,008 → 137,023 | -2.1% | 5.65 → 5.52 | -2.3% |
| ripgrep · macOS arm64 | 15.2.0 → 16.0.0 | 1,539,933 → 1,517,177 | -1.5% | 41.46 → 38.02 | -8.3% |
| GitHub CLI · macOS arm64 | 2.104.0 → 2.105.0 | 8,612,836 → 8,270,990 | -4.0% | 523.60 → 539.35 | 3.0% |

## Inputs and verification

The inputs were the official standalone release executables. For SQLite we extracted `sqlite3` from each SQLite tools ZIP. For ripgrep and GitHub CLI we extracted `rg` and `gh` from their release archives. For jq we used the published Mach-O file directly. We preserved the binaries as distributed, including embedded metadata and signatures. [SQLite releases](https://sqlite.org/chronology.html) · [jq releases](https://github.com/jqlang/jq/releases) · [ripgrep releases](https://github.com/BurntSushi/ripgrep/releases) · [GitHub CLI releases](https://github.com/cli/cli/releases)

For every pair, VCDiffSharp decoded the patch and compared the result byte-for-byte with the target executable. xdelta3 3.2.2 and Google open-vcdiff also reconstructed the exact target from each of the 14 before/after patches. The allocating and caller-provided writer APIs produced identical patches. All repeated encodes of a given pair/build produced the same patch SHA-256.

## Timing

Both versions were built from Git snapshots with the same .NET 8 Release harness. The baseline was `202fe94` and the optimized build was `bbfcf09`. The harness loaded source and target bytes into memory and reused an `ArrayBufferWriter<byte>` sized to the patch. It selected `VcdiffEncodeMode.Fast` and `MemoryMode.Default`. File I/O and verification were outside the timer. The allocated bytes column records managed encoder allocations after warm-up.

Each encoder process warmed each binary pair for at least 1.8 seconds and took the median of nine timed batches. Batch sizes were calibrated to at least 25 ms. The table reports the median of three such process medians per build, measured in before/after/after/before/after/before order. A negative time change means faster encoding. The six small pairs fit in one target window each. The GitHub CLI target was 39,019,376 bytes and used five windows.

The macOS and Linux SQLite patch-release comparisons had faster encode times in every optimized run than in every baseline run. Timings for the SQLite feature release and ripgrep overlapped across builds. The measured allocation was 0 bytes per call after warm-up in the main comparison.

The primary GitHub CLI comparison measured a 3.0% increase in median encode time and a 341,846-byte decrease in patch size. In a separate GitHub CLI follow-up, each timed batch contained at least four encodes. The two process medians were 591.0–600.9 ms before and 519.1–540.7 ms after (CSV runs 4 and 5 for each build).

[Per-run measurements, input hashes, patch hashes, and exact download URLs](Fast-release-binaries-net8-arm64.csv)

To reproduce the optimized macOS SQLite patch-release measurement from this checkout, extract the arm64 CLI for each version and pass the two executables to the focused benchmark.

```sh
for version in 3540000 3540100; do
  curl -fsSL "https://sqlite.org/2026/sqlite-tools-osx-arm64-${version}.zip" -o "sqlite-${version}.zip"
  unzip -p "sqlite-${version}.zip" sqlite3 > "sqlite3-${version}"
done
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks -- --fast-only sqlite3-3540000 sqlite3-3540100
```
