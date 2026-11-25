# More Fast source-routing experiments

Three benchmark series tested ways to reduce source work, including changes that may sacrifice patch size. The earlier first-byte tail mask remains the leading byte-preserving candidate on SQLite. The early-stop prototype is an alternative if a modest size increase is acceptable.

## End-to-end encode time

Each cell is median milliseconds per encode, including constructing and releasing the index. The macOS feature pair is SQLite 3.53.4 → 3.54.0. Both patch pairs are SQLite 3.54.0 → 3.54.1. The last input is the prior synthetic shared-header case. Compare times within a series.

| Series | Algorithm | Moby-Dick | macOS feature | macOS patch | Linux patch | Shared headers |
|---|---|---:|---:|---:|---:|---:|
| I | Shipping Fast | 0.802 | 17.740 | 5.918 | 7.523 | 0.836 |
| I | First-byte tail mask (prior) | 0.812 | 14.354 | 4.987 | 6.276 | 0.846 |
| I | Contiguous SIMD postings | 1.318 | 17.018 | 6.152 | 7.826 | 0.791 |
| I | Two-sided tail mask | 1.001 | 16.349 | 5.853 | 7.269 | 0.851 |
| I | Stop after 16 misses | 0.714 | 14.010 | 4.944 | 6.026 | 0.124 |
| II | Shipping Fast | 0.784 | 17.772 | 5.929 | 7.506 | 0.843 |
| II | First-byte tail mask (prior) | 0.813 | 14.705 | 5.055 | 6.337 | 0.845 |
| II | Global three-byte filter | 1.216 | 19.265 | 6.622 | 9.001 | 0.831 |
| II | Global filter + tail mask | 1.247 | 17.614 | 6.189 | 8.021 | 0.844 |
| II | Exact-seed open addressing | 1.829 | 19.965 | 7.814 | 9.983 | 0.835 |
| III | First-byte tail mask (prior) | 0.826 | 14.203 | 4.954 | 6.324 | 0.854 |
| III | Tail mask + end marker | 1.331 | 15.620 | 6.048 | 7.411 | 1.156 |
| III | Current byte tag + end marker | 1.460 | 19.315 | 7.056 | 8.698 | 1.157 |

## Search work

These are deterministic counts on the macOS SQLite feature update. Candidate positions count individual source positions examined. SIMD lanes and open-addressing slot checks are listed separately because both can move work out of the candidate loop. Linked traversals count next-link loads, including loads of a terminating zero. A live head lookup is additional work.

| Algorithm | Positions | Match extensions | Next-link reads | Other search work | Patch bytes |
|---|---:|---:|---:|---|---:|
| Shipping Fast | 909,731 | 438,384 | 909,297 | — | 707,780 |
| First-byte tail mask (prior) | 673,640 | 438,384 | 673,211 | — | 707,780 |
| Two-sided tail mask | 755,461 | 437,036 | 755,028 | — | 707,780 |
| Contiguous SIMD postings | 604,280 | 438,384 | 0 | 909,731 metadata entries checked | 707,780 |
| Global three-byte filter | 746,113 | 437,056 | 745,683 | 347,863 probes rejected before lookup | 707,780 |
| Global filter + tail mask | 641,299 | 437,056 | 640,871 | 347,863 probes rejected before lookup | 707,780 |
| Exact-seed open addressing | 601,624 | 435,789 | 601,197 | 484,175 occupied slots checked | 707,774 |
| Tail mask + end marker | 673,640 | 438,384 | 606,744 | — | 707,780 |
| Current byte tag + end marker | 604,771 | 438,384 | 686,975 | 909,731 metadata entries checked | 707,780 |
| Stop after 16 misses | 518,941 | 173,757 | 513,496 | — | 712,744 |

## What the experiments change

- **Contiguous SIMD postings.** Count landmarks by original bucket, prefix-sum the counts, then scatter positions into one contiguous range per bucket in original source order. Store the exact first byte in each 32-bit descriptor. Process four descriptors together and dereference positions only for matching lanes, retaining the original head plus 256-hop budget. For sources below 4 KiB use the original table. The second pass over source bytes and the directory scan are part of the measured build. On the macOS feature update, the layout reduced individual position inspections by 33.6% versus shipping Fast, yet the single-pass packed mask was faster.
- **Two-sided tail mask.** Split the eight spare bits into two four-class masks. One summarizes three bytes starting at the landmark and the other summarizes two bytes preceding it plus its first byte. A COPY of length four or greater must match one of those triples. When fewer than two pending literals are available, it must match the forward triple. This permits safe tail rejection before there is an incumbent. In practice the four-class masks saturate more easily and computing them costs more than using the first byte alone.
- **Global three-byte filter.** Mark the same two kinds of triples in separate 64 KiB bit arrays while preprocessing the source. A negative at query time can avoid the primary lookup entirely. On the macOS feature pair it rejected 347,863 of 652,636 probes, but many such probes already hit empty or cheap buckets. Maintaining and reading the filters cost more than the work saved. Adding it to the previous packed mask also lost to that mask alone.
- **Exact-seed open addressing.** Linear probing assigns a home slot to each distinct eight-byte seed and chains equal seeds. The prototype uses the original table when its capacity would take linear probing above 75% load. Distinct-key checks are extra source reads. Fast can ordinarily find a useful partial seed match by extending backward, and hash collisions normally count toward its 256-hop cap. Exact-seed grouping changes both effects. The real-file patch changes ranged from 12 bytes smaller to four bytes larger.
- **Stop after a plateau.** Once a source match of at least eight bytes has been found, stop after 16 consecutive candidates fail to increase its length. A later better candidate may be lost. Versus shipping Fast this added 38 bytes on Moby-Dick and 4,964 / 1,006 / 2,827 bytes on the three SQLite pairs. The shared-header patch stayed identical.
- **Terminal links.** For 32-bit source tables, use a zero-based index in 23 bits and reserve one bit for the last node. This works through the 64 MiB source-segment limit. One variant also stores the first-byte tail mask. The other uses a current-node byte tag and always verifies surviving candidates. The separate terminal marker avoids fetching the last link, including in singleton buckets. It adds mask decoding and an exit test to the hot loop.

## Verification and reproduction

All runs used .NET 8.0.25 Release, Arm64 on an Apple M3 Max, Fast, default memory, and a preallocated writer. The timer includes index construction and encoding. Reading files and validating results were outside the timer. The order of configurations rotated across five fresh processes per configuration in series I and three per configuration in series II and III. Each process warmed each input for at least 1.2 seconds and measured nine batches. Tables report medians of the process medians. Work counters came from separate untimed builds.

All benchmark patches decoded to their original target bytes. The output-preserving variants also matched the baseline bytes for all five benchmark inputs and for 480 additional deterministic cases across index widths and both memory modes. The lossy policies are checked for correct reconstruction. Size and speed conclusions are specific to these data and this Arm64 machine.

Apply a single [prototype patch](../Experiments/FastCandidateRouting/alternatives/) to an isolated checkout of `2e0ba85` with `git apply --unidiff-zero`. The variant names in the raw file correspond to the patch filenames. The earlier `bitmap` control is in the parent directory. Use `dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks -- --fast-only source.bin target.bin` to repeat a file-pair measurement. [Per-run timings, candidate counts and patch hashes](Fast-candidate-routing-alternatives-net8-arm64.csv) · [SQLite file provenance](Fast-release-binaries-net8-arm64.md)

xdelta3 3.2.2 also reconstructed the exact targets for all 50 benchmark patches. The synthetic early stop took 0.124 ms against 0.836 ms for the corresponding baseline, but on real files its advantage over the existing packed mask was much smaller. For the two macOS updates the early stop saved only 0.04–0.34 ms over the mask and increased patch size. The terminal-bit variants did cut final-link reads, but both lost time against the simpler mask on all four real pairs.
