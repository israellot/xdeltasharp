# Adjacent eight-byte hash in the Fast source index

A cached hash of the next source word reduced a few match extensions and saved zero chain visits on all five inputs.

| Input | Visits in either build | Tag rejections | Extensions before → cached | Avoided | Baseline ms | Cached ms | Direct word ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| Moby-Dick | 26,146 | 1,111 | 10,311 → 10,241 | 0.7% | 0.785 | 0.798 | 0.776 |
| SQLite macOS 3.53.4 → 3.54.0 | 909,731 | 66,176 | 438,384 → 426,820 | 2.6% | 17.403 | 17.315 | 17.260 |
| SQLite macOS 3.54.0 → 3.54.1 | 266,430 | 20,580 | 126,054 → 122,981 | 2.4% | 5.711 | 5.709 | 5.559 |
| SQLite Linux 3.54.0 → 3.54.1 | 387,987 | 32,231 | 208,715 → 202,643 | 2.9% | 7.357 | 7.295 | 7.183 |
| Synthetic shared headers | 136,965 | 255 | 131,149 → 130,894 | 0.2% | 0.826 | 0.917 | 0.850 |

A visit counts one primary collision-chain entry examined. An extension compares the actual forward and backward match after the existing first-byte and incumbent-length checks. Tag rejections include candidates too close to the end of a span to supply the required word. On the macOS patch update the tag rejected 20,580 entries, but only 3,073 would otherwise have reached extension.

## Filter and memory

Each source landmark at p receives the 64-bit hash of bytes p+8 through p+15 in a parallel pooled array. Backwards construction reuses the hash already computed for the following landmark. A target probe computes its tag once on demand. A third build reads and compares the adjacent eight-byte words directly. All use the original chain order and hop budget.

If adjacent words differ, the forward match can still span 15 bytes. If B bytes of backward extension are possible, we can safely reject only when the incumbent already covers at least 15+B bytes. Ties keep the earlier candidate. All accepted COPYs are verified against the source bytes.

The cache adds eight bytes per complete eight-byte source block, about 1.20 MiB on the books and 1.94–1.97 MiB on the SQLite files before pool rounding. The extra rental on this runtime is 2 MiB for each of those inputs. The direct-word comparison adds no index storage.

## Measurements

Apple M3 Max, .NET 8.0.25 Release, Fast with default memory settings, afterburner off. Three fresh processes per build, rotated order, at least 1.2 seconds of warm-up per pair and nine timed batches per process. The table gives medians of process medians. File I/O and verification were outside the timer. Index construction was timed. Counters came from separate instrumented builds. The synthetic source has 256 records with a shared eight-byte header and varying following words. Its 512 target records reuse the first record’s 16-byte prefix with different endings.

Every build produced byte-identical patches on all five pairs. xdelta3 independently reconstructed all five targets. [Per-run timings and hashes](Fast-adjacent-hash-net8-arm64.csv) · [SQLite input files](Fast-release-binaries-net8-arm64.md)

## Reproduce

Apply the [experimental patch](../Experiments/FastSourceBlockSize/adjacent-tag.patch) in an isolated checkout of `0ebd5d3`. The unmodified checkout provides the baseline. The patch enables direct-word comparison by default. Use the following switch for cached hashes.

```sh
git apply --unidiff-zero /path/to/adjacent-tag.patch
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks \
  -p:FastCacheAdjacentHash=true -- --fast-only source.bin target.bin
```
