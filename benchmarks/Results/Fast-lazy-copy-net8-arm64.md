# Selective lazy matching in Fast

Fast reduced the Moby-Dick patch from **34,568 to 29,959 bytes**. Encoding took **0.731–0.738 ms**, down from **0.880–0.888 ms** at commit `202fe94`. The change preserves the source-only search and the 256-hop cap.

## Search policy

For an indexed source and a target window of at least 64 bytes, Fast checks up to eight probe positions at the start. For windows of at least 1,024 bytes it can also check eight near the middle. It looks for 12 matching bytes near the expected source position or 48 matching bytes at a more distant landmark. Sampling reads at most the bucket head per probe. A failed sample keeps the ordinary greedy policy for that window.

For a selected window, a COPY of at least 12 bytes supplies a predicted source-to-target offset. Before committing a competing COPY of length 4–18, Fast can verify one eight-byte seed one byte ahead on the predicted alignment. It switches only when the alternative reaches farther and covers at least two more matched bytes. After a long COPY or a COPY on a locally stable alignment, it can also resume at the same offset after one literal byte, provided at least 12 bytes match. An upcoming run of repeated bytes goes through normal RUN selection. These checks reuse the current source segment and add no index.

The source matcher also skips a candidate when even its maximum backward extension would require a mismatching forward byte to beat the incumbent. A source-free window scans only for literals and RUNs.

## Measurements

Apple M3 Max, .NET 8.0.25 Release, `MemoryMode.Default`. The baseline and candidate used the same in-memory inputs and preallocated output writer. Each process warmed each case, then measured nine batches and verified reconstruction outside the timed loop. Runs used before/after/after/before order. Each range gives the two independently measured batch medians. Times below are in microseconds. Every writer case measured zero managed bytes per encode after warm-up.

| Input | Before bytes | After bytes | Before µs | After µs |
|---|---:|---:|---:|---:|
| Moby-Dick editions | 34,568 | 29,959 | 879.8–888.4 | 731.2–738 |
| Git archives | 48,952 | 48,924 | 797.1–832.1 | 725.9–749 |
| Managed assemblies | 23,774 | 23,567 | 143.9–157.7 | 143.5–144.2 |
| 16 B, one edit | 20 | 20 | 0.103–0.106 | 0.106–0.111 |
| 64 B, one edit | 22 | 22 | 0.148–0.149 | 0.103–0.104 |
| 2,047 B, one edit | 27 | 27 | 0.754–0.757 | 0.714–0.719 |
| 512 KiB, one edit | 32 | 32 | 143.1–143.6 | 143.6–144.6 |
| Unrelated 256 KiB | 262,166 | 262,166 | 2,134.3–2,210.1 | 1,887.6–2,024.1 |
| Source-free random 256 KiB | 262,166 | 262,166 | 464.5–466.2 | 212.4–215.6 |
| 1 MiB uniform run | 19 | 19 | 32.67–34.7 | 34.23–34.25 |
| 256 reordered records | 1,037 | 1,037 | 9.92–10.01 | 9.1–9.2 |
| Edit every 15 bytes | 93,958 | 69,935 | 1,661–1,705.9 | 433.6–435.5 |
| Edit every 31 bytes | 42,313 | 42,313 | 671.1–686 | 218.9–221.3 |
| Edit every 127 bytes | 10,351 | 10,351 | 210.1–219.9 | 107.1–107.4 |
| 5,000 shuffled chunks | 34,564 | 34,564 | 445.3–466.9 | 429.3–492.3 |

The book pair is DeltaSharp’s `pg15.txt` → `pg2701.txt`. Git archives were generated with `git archive --format=tar` for VCDiffSharp commits `79eff59` and `202fe94`. The assembly pair used Release builds of `ab0c875` and `202fe94`. Synthetic cases used deterministic random bytes. The periodic edits flip one byte at each specified interval in a 256 KiB source. The shuffled case assembles random source slices 4–39 bytes long interleaved with literal bytes.

On shuffled chunks the candidate was slower in one run and faster in the other. The 16-byte case cost a few additional nanoseconds in one run. This comparison establishes the benefit on related files on this machine. Absolute performance across inputs, architectures and runtime versions can vary.

A full extra hash-chain search one byte ahead produced 31,118 bytes on Moby-Dick and took about 8% longer than the baseline in the initial trial. Trying seven positions reduced the patch to 23,650 bytes and took about 47% longer. Buffering multiple operations also imposed a noticeable penalty on reordered records. Those policies were excluded from Fast.

A separate deterministic sweep covered ordinary edits, mixed literals/runs/copies, and sources containing competing short landmarks near edits. Across 1,440 pairs and both memory settings, 712 patches were smaller, 2,168 were identical in size, and none were larger. Every result reconstructed its target. Both xdelta3 and open-vcdiff independently verified the Moby-Dick patch and a multi-window binary patch.

To time either public Fast output overload on your own files, the focused benchmark measures both output and memory policies after warm-up:

```sh
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks -- --fast-only source.bin target.bin
```
