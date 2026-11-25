# Fast with overlapping eight-byte source landmarks

Reducing source spacing from eight to four bytes shrank every patch in this comparison. With the default memory policy, the denser index also took longer on three of the four pairs. The Linux SQLite patch update became both smaller and faster.

## Default memory policy

| Input | 8 B stride patch | 4 B stride patch | Patch change | 8 B stride ms | 4 B stride ms | Time change |
|---|---:|---:|---:|---:|---:|---:|
| Moby-Dick | 29,959 | 26,242 | -12.4% | 0.78 | 1.15 | 47.7% |
| SQLite macOS 3.53.4 → 3.54.0 | 707,780 | 667,671 | -5.7% | 17.14 | 19.57 | 14.2% |
| SQLite macOS 3.54.0 → 3.54.1 | 235,906 | 222,886 | -5.5% | 5.68 | 6.20 | 9.1% |
| SQLite Linux 3.54.0 → 3.54.1 | 289,029 | 274,191 | -5.1% | 7.29 | 6.75 | -7.4% |

A match needs a whole eight-byte seed at an indexed offset. Across arbitrary alignments, an identical region of 15 bytes always contains such a seed with stride eight. With stride four, 11 bytes suffice. This describes seed placement. Collisions, the hop limit, and greedy choices still affect which COPY the encoder emits.

## LessMemory

| Input | 8 B stride patch | 4 B stride patch | Patch change | 8 B stride ms | 4 B stride ms | Time change |
|---|---:|---:|---:|---:|---:|---:|
| Moby-Dick | 29,960 | 26,245 | -12.4% | 0.83 | 1.13 | 36.6% |
| SQLite macOS 3.53.4 → 3.54.0 | 707,788 | 667,674 | -5.7% | 21.77 | 23.87 | 9.7% |
| SQLite macOS 3.54.0 → 3.54.1 | 235,904 | 222,891 | -5.5% | 6.93 | 7.25 | 4.6% |
| SQLite Linux 3.54.0 → 3.54.1 | 289,030 | 274,198 | -5.1% | 8.73 | 8.09 | -7.4% |

On these four sources, all entries were 32-bit. The default source-index rental grew from 4 MiB to 8 MiB. With LessMemory it grew from 2 MiB to 4 MiB. These are array capacities in the local .NET pool. The warmed writer recorded 0 managed bytes allocated per encode in all configurations.

Four-byte stride with LessMemory also serves as a fixed-bucket-count control against the shipping eight-byte stride with default memory. Both have 524,288 bucket heads and use a 4 MiB pool rental on these inputs. The denser build stores more collision links in that rental. Its patch sizes were within seven bytes of four-byte stride with the larger bucket table. It took 1.13, 23.87, 7.25 and 8.09 ms respectively on the four pairs.

## Search work

Counters collected in separate untimed runs with the default memory policy show why double the landmarks need not mean double the search work. On Linux SQLite, ordinary probes fell from 244,938 to 203,390 and candidate visits from 387,987 to 276,774. On Moby-Dick, probes also fell (17,361 to 12,311), but candidate visits increased from 26,146 to 35,052. Index construction is also included in the encode timings. The window-level COPY prediction gate was enabled in every run.

## Method

Apple M3 Max, macOS arm64, .NET 8.0.25 Release. Two isolated builds of commit `d0570cc` ran ordinary Fast encoding. The experimental build changed the count and spacing of complete source landmarks and the conversion from landmark index back to byte offset. Hashing still reads eight bytes at a time and uses the same function. Both builds used the same 256-hop cap, lazy COPY policy, window size, source segment, and writer. The afterburner was off.

For each input longer than or equal to eight bytes, the four-byte prototype stores `1 + (length - 8) / 4` landmarks using integer division. Tiny direct-search inputs bypass the table. For the book source, Fast indexed 156,743 positions at stride eight and 313,486 at stride four. The default index requested 2,724,124 and 5,448,248 bytes respectively, before pool rounding.

Input files and hashes match the [previous block-size experiment](Fast-source-block-size-net8-arm64.md) and [SQLite executable manifest](Fast-release-binaries-net8-arm64.csv). Each build and memory combination ran in four fresh processes, with timing order rotated across rounds. Inputs were already in memory and the output buffer was preallocated. Each input was warmed for at least 1.2 seconds, then timed in nine batches. The tables report medians of four process medians. File I/O and validation were outside the timed region. All 16 distinct patches reconstructed byte-for-byte in VCDiffSharp, xdelta3 3.2.2 and open-vcdiff. [Raw timings, index capacities, and patch hashes](Fast-overlapping-landmarks-net8-arm64.csv)

## Reproduce

Apply the [experimental stride patch](../Experiments/FastSourceBlockSize/stride4.patch) to a separate checkout of `d0570cc`. Run the focused benchmark on both the original and patched builds.

```sh
git apply --unidiff-zero /path/to/stride4.patch
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks -- --fast-only source.bin target.bin
```
