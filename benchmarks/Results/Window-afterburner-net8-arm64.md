# Window afterburner on Moby-Dick and SQLite

These historical measurements cover level 3 (listed as Default) and the retired Fast and Exhaustive matchers. Each pair compares the same encoder and memory settings with and without the optional operation pass. Sizes include the file header and all window framing. Times are milliseconds per encode into a reused destination.

| Input | Mode | Original bytes | Optimized bytes | Size change | Original ms | Optimized ms |
|---|---|---:|---:|---:|---:|---:|
| Moby-Dick | Fast | 29,959 | 24,687 | -17.6% | 0.79 | 1.02 |
| Moby-Dick | Default | 21,287 | 19,833 | -6.8% | 1.17 | 1.43 |
| Moby-Dick | Exhaustive | 20,737 | 19,684 | -5.1% | 12.22 | 12.93 |
| SQLite macOS 3.53.4 → 3.54.0 | Fast | 707,780 | 702,670 | -0.7% | 17.24 | 19.80 |
| SQLite macOS 3.53.4 → 3.54.0 | Default | 577,726 | 560,246 | -3.0% | 28.14 | 37.59 |
| SQLite macOS 3.53.4 → 3.54.0 | Exhaustive | 562,528 | 544,429 | -3.2% | 218.15 | 224.95 |
| SQLite macOS 3.54.0 → 3.54.1 | Fast | 235,906 | 233,649 | -1.0% | 5.74 | 6.76 |
| SQLite macOS 3.54.0 → 3.54.1 | Default | 201,908 | 198,723 | -1.6% | 7.82 | 9.61 |
| SQLite macOS 3.54.0 → 3.54.1 | Exhaustive | 224,169 | 207,219 | -7.6% | 106.07 | 109.62 |
| SQLite Linux 3.54.0 → 3.54.1 | Fast | 289,029 | 282,632 | -2.2% | 7.29 | 8.61 |
| SQLite Linux 3.54.0 → 3.54.1 | Default | 243,153 | 238,692 | -1.8% | 8.79 | 11.27 |
| SQLite Linux 3.54.0 → 3.54.1 | Exhaustive | 260,482 | 250,362 | -3.9% | 144.44 | 147.36 |

Apple M3 Max, .NET 8.0.25 Release, default memory policy. Inputs came from the DeltaSharp Moby-Dick fixtures and the [SQLite release executable measurements](https://github.com/israellot/xdeltasharp/blob/05c751b5be3097b7a0cf3bfacc0460ac715d51db/benchmarks/Results/Fast-release-binaries-net8-arm64.md). Each encoder ran in both measurement orders, warmed up for at least 350 ms, and timed seven batches per order. The table gives the average of the two batch medians. File I/O, input setup, and output verification were outside the timed region. All twelve optimized files were independently decoded with xdelta3 3.2.2 and open-vcdiff and compared byte-for-byte with their targets.

To measure the pass on another input pair at levels 0 through 9, run the current benchmark.

```sh
dotnet run -c Release --project benchmarks/XDeltaSharp.Benchmarks -- --afterburner source.bin target.bin
```
