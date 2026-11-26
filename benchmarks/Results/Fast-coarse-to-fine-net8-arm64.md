# Fast with 32-byte source landmarks followed by 8-byte refinement

A coarse search helped patch size on Moby-Dick. It cost more encode time on every tested pair. On the SQLite updates the second-stage patches stayed close to ordinary 8-byte Fast in size, including cases where locking in the coarse copies made the patch larger.

## Dense coarse search

Cells contain patch bytes / median milliseconds. The columns give the minimum COPY length accepted by the coarse search. All three use the same 32-byte hash seed and source stride.

| Input | 8-byte Fast | Minimum 32 B | Minimum 64 B | Minimum 128 B |
|---|---:|---:|---:|---:|
| Moby-Dick | 29,959 / 0.74 | 25,139 / 1.40 | 26,148 / 1.73 | 27,679 / 2.00 |
| SQLite macOS 3.53.4 → 3.54.0 | 707,780 / 16.92 | 710,494 / 30.74 | 706,239 / 34.19 | 707,231 / 46.20 |
| SQLite macOS 3.54.0 → 3.54.1 | 235,906 / 5.47 | 241,120 / 13.67 | 236,548 / 15.52 | 236,077 / 26.78 |
| SQLite Linux 3.54.0 → 3.54.1 | 289,029 / 7.07 | 289,809 / 15.05 | 287,796 / 17.46 | 288,807 / 23.60 |

At the 32-byte cutoff, coarse copies covered 96.4% of the book target, 47.2% of the macOS feature update, 77.2% of the macOS patch update, and 72.9% of the Linux patch update. Every input still needed the ordinary 8-byte source index. Even the book pair required 1,687 anchors and 70,137 coarse probes. The macOS feature update needed 1,193,244 coarse probes. Higher cutoffs can add work because the scanner continues past matches that are too short to reserve.

## Sparse coarse search

The coarse target probe advanced by 3 or 7 bytes after unsuccessful lookups. Both steps are coprime to the 32-byte source spacing. This allows the probe to cycle through source alignments but can miss shorter matching regions. The 8-byte refinement searches those gaps. A fresh baseline was measured in this series.

| Input | 8-byte Fast | Minimum 64 B, step 3 | Minimum 128 B, step 7 |
|---|---:|---:|---:|
| Moby-Dick | 29,959 / 0.78 | 27,260 / 1.32 | 27,727 / 1.25 |
| SQLite macOS 3.53.4 → 3.54.0 | 707,780 / 17.43 | 707,137 / 23.88 | 707,360 / 23.58 |
| SQLite macOS 3.54.0 → 3.54.1 | 235,906 / 5.67 | 237,070 / 11.20 | 236,126 / 10.66 |
| SQLite Linux 3.54.0 → 3.54.1 | 289,029 / 7.04 | 288,584 / 12.25 | 288,934 / 11.55 |

Even with step 7, encoding took 1.35–1.88× as long as the matching baseline. On the three SQLite pairs, patch size changed by less than 0.1% in either direction at that setting.

## Method

All runs used the same source and target files as the [single-block-size comparison](Fast-source-block-size-net8-arm64.md). The instrumented prototypes used 8 MiB target windows, the default bucket count, and up to 256 collision links. A coarse match requires an exact 32-byte seed and can extend forwards and backwards up to the preceding reserved match. After accepting a coarse COPY, the parser jumps to its end. It refines the gap immediately before emitting the anchor, so the two logical passes share a writer and address cache and need no list of anchors. Each eligible gap searches the entire selected source segment through the 8-byte index. Fragments shorter than eight bytes use literal and RUN handling. The fine index is initialized on demand and reused within the segment. The fine matcher uses the ordinary window-level prediction setting, starts a gap with the preceding anchor’s alignment when available, and keeps all instructions within the gap.

Apple M3 Max, .NET 8.0.25 Release. The harness loaded inputs once and measured in-memory encoding into a reused buffer after at least 1.2 seconds of warm-up. Each process recorded a median across nine batches. The table gives medians across four rotated process runs for the dense comparison and three for the sparse comparison. Timings include index construction, both searches, and patch writing. All 24 configuration/input pairs reconstructed byte-for-byte with VCDiffSharp and xdelta3 3.2.2. Repeated runs produced identical patch hashes. [Raw timings](Fast-coarse-to-fine-net8-arm64.csv)

## Reproduce

Apply the [experimental patch](https://github.com/israellot/xdeltasharp/blob/f405b3ea3c0ddcd2e2dbb1e57cd634dd07c88693/benchmarks/Experiments/FastSourceBlockSize/coarse-to-fine.patch) in an isolated checkout of commit `19ca50a`. The plain checkout provides the 8-byte baseline. The following example selects 64-byte minimum matches and a target step of 3. The default experiment settings are 32 and 1. The reported measurements use the Fast, Default-memory, writer path.

```sh
git apply --unidiff-zero /path/to/coarse-to-fine.patch
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks \
  -p:CoarseMinimum=64 -p:CoarseStep=3 \
  -- --fast-only source.bin target.bin
```
