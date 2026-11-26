# Fast source block size on Moby-Dick and SQLite

**8 bytes produced the smallest patch on all four pairs.** Full 16-byte blocks gave the fastest median encode time on every pair. Full 32-byte blocks were slower than 16 and produced larger patches on every pair.

## Full-width hash seeds

Each cell gives encoded patch bytes and median milliseconds per encode. SQLite platforms identify the input executables.

| Pair | 8 B | 16 B | 32 B |
|---|---:|---:|---:|
| Moby-Dick | 29,959 B / 0.74 ms | 31,953 B / 0.50 ms | 45,911 B / 0.59 ms |
| SQLite macOS 3.53.4 → 3.54.0 | 707,780 B / 16.57 ms | 904,078 B / 13.02 ms | 1,116,529 B / 14.07 ms |
| SQLite macOS 3.54.0 → 3.54.1 | 235,906 B / 5.48 ms | 316,543 B / 5.10 ms | 447,603 B / 6.19 ms |
| SQLite Linux 3.54.0 → 3.54.1 | 289,029 B / 6.96 ms | 400,968 B / 6.57 ms | 592,491 B / 8.36 ms |

Relative to the current 8-byte encoder, full 16-byte blocks cut time by 31.6% on Moby-Dick and increased patch size by 6.7%. On SQLite they cut time by 5.6–21.4% and increased patch size by 27.7–38.7%. At 32 bytes, the Linux patch-release delta more than doubled in size and took 20.0% longer to encode.

On the macOS SQLite patch release, xdelta3 reported data-section sizes of 133,683, 232,767 and 383,379 bytes at 8, 16 and 32 bytes. Instruction and address sections both shrank. The extra literal and RUN payload outweighed those savings. Prediction was enabled for all four input pairs at every setting. The 32-byte build indexed roughly one quarter as many landmarks as the baseline and used 16-bit entries on all four sources. The 8- and 16-byte builds used 32-bit entries.

## Spacing-only controls

These builds retained the eight-byte hash while spacing landmarks 16 or 32 bytes apart. Percentages compare against the current 8-byte encoder.

| Pair | 16 B spacing · patch change | Encode time change | 32 B spacing · patch change | Encode time change |
|---|---:|---:|---:|---:|
| Moby-Dick | +28.6% | -12.3% | +67.1% | -8.7% |
| SQLite macOS 3.53.4 → 3.54.0 | +8.7% | -10.7% | +20.6% | -15.9% |
| SQLite macOS 3.54.0 → 3.54.1 | +9.9% | +0.1% | +26.8% | +2.5% |
| SQLite Linux 3.54.0 → 3.54.1 | +12.8% | +5.9% | +34.6% | +12.9% |

For SQLite, narrower seeds at the same spacing recovered some patch size. On Moby-Dick, the full-width seeds gave smaller patches at both spacings. The effect of seed width depends on the input and the resulting greedy match decisions.

VCDiffSharp, xdelta3 3.2.2, and open-vcdiff each reconstructed the expected target from all 20 distinct patches. For each configuration and input, patch bytes were deterministic across the five processes. The warmed writer measured 0 managed bytes allocated per encode in every run. [All 100 process measurements, patch hashes, and bucket counts](Fast-source-block-size-net8-arm64.csv)

## What changed in the experiment

The 8 B baseline came from an unmodified VCDiffSharp checkout at `1f9f703`. Each comparison build used a compile-time constant for source block size. A 16 B build indexes one landmark per complete 16 source bytes. A 32 B build indexes one per complete 32 bytes. The primary runs hashed the full block: extra little-endian eight-byte words were folded into the first word by rotating left 19 bits and XORing the next word, then applying the existing 64-bit hash finalizer. The controls retained the original eight-byte bucket hash and changed only the source spacing and resulting index size.

All encodes used explicit Fast, default memory policy, the 256-hop collision limit, the same backward and forward extension, and the existing lazy prediction policy, including its eight-byte verification. Target probes still advance byte by byte while searching. The block size controls source indexing; discovered COPY instructions can span arbitrary byte lengths. Index entry width was selected normally from the number of complete blocks.

## Timing and inputs

Apple M3 Max, macOS arm64, .NET 8.0.25 Release. Each of five builds ran in five fresh processes. The order was rotated across rounds so every build occupied each timing slot once. Each process encoded all four source/target pairs in memory, warmed each pair for at least 1.2 seconds, then timed nine batches using a reusable output buffer. The reported time is the median of the five process-level medians. It includes source-index construction, matching, and writing the patch. File reads, downloads, verification, and process startup were outside the timer. A timing difference close to the range of repeated runs should be treated as inconclusive.

Moby-Dick uses the DeltaSharp fixtures `pg15.txt` (SHA-256 `7da8e626fa38865c93f4d356e51b9eaa3434ace8ceb95e3a9ab8dc609c8670bd`) and `pg2701.txt` (`f29bf5abe1524aaa1d6e81c3a9e6bad9dca2433fa13d550b013e94589d7d65ac`). SQLite uses the extracted, as-distributed executables from the [release-binary benchmark](Fast-release-binaries-net8-arm64.md). Its [input manifest](Fast-release-binaries-net8-arm64.csv) includes the release URLs and executable hashes. The Linux x64 label identifies the input binary. The encoder ran on Arm64 throughout.

## Reproducing a variant

Apply the [experimental patch](https://github.com/israellot/xdeltasharp/blob/f405b3ea3c0ddcd2e2dbb1e57cd634dd07c88693/benchmarks/Experiments/FastSourceBlockSize/block-size.patch) to an isolated copy of `1f9f703`. It adds benchmark-only build properties. Use the original checkout for the baseline. The following example measures a 16-byte block and 16-byte seed on a supplied file pair. Set `FastBenchmarkWideSeed=false` for the spacing-only control. For this comparison read the `Fast,Default,writer` result.

```sh
git apply --unidiff-zero /path/to/block-size.patch
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks \
  -p:FastBenchmarkStride=16 -p:FastBenchmarkWideSeed=true \
  -- --fast-only source.bin target.bin
```
