# Encoder and benchmark notes

## Implementation map

| Type | Responsibility |
| --- | --- |
| `Vcdiff` | Public entry points, file header, window sequence, and total output limit. |
| `Encoding/WindowEncoder` | Select source segments and choose ADD, RUN, or COPY under the requested search policy. |
| `Encoding/LevelParser` | Apply the numbered matcher profile and resolve pending instructions before writing. |
| `Encoding/WindowWriter` | Frame the window and write its data, instructions, and COPY addresses. |
| `Encoding/SourceMatchFinder` | Search an indexed source segment. The encoder can reuse the index across windows that select that segment. |
| `Encoding/TargetMatchFinder` | Search four-byte seeds at earlier positions in the current target window, allowing overlapping COPY operations. |
| `Encoding/CopyMatch` | Carry a candidate’s read offset, target write offset, and length from either finder. |
| `Decoding/WindowReader` | Parse file and window headers, validate the dictionary range, and bound the three section cursors. |
| `Decoding/WindowDecoder` | Execute the data, instruction, and address sections against the selected dictionary and the target window being reconstructed. |
| `Format/AddressCache` | Resolve COPY addresses during decoding using per-window NEAR and SAME caches. |
| `Format/DefaultCodeTable` | Describe the single and paired instructions represented by each of the 256 default opcodes. |

Run `dotnet test -c Release` for the [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284.html) vectors, malformed inputs, and [independent xdelta3 and open-vcdiff fixtures](../tests/XDeltaSharp.Tests/Fixtures/README.md).

## Encoding levels

Both allocating and buffer-writer APIs accept an integer compression level from **0 to 9** and an optional memory policy. Omitting the level, or choosing `VcdiffEncodeMode.Default`, uses **level 3**.

```csharp
byte[] patch = Vcdiff.Encode(source, target, compressionLevel: 9);
Vcdiff.Encode(source, target, destinationWriter, compressionLevel: 3, memoryMode: MemoryMode.LessMemory);
```

The level mapping follows xdelta3 3.2.2. The target seed is four bytes, the indexed source seed is nine bytes, and runs start at eight equal bytes. The lazy limit controls which short matches get another look at the next position. An improving lazy match must extend past the pending match. The stop length ends a target-chain search early. [Upstream CLI mapping](https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-main.h) · [Matcher settings](https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3-cfgs.h)

| Level | Source stride | Target candidates | Lazy candidates | Lazy limit | Stop length |
|---|---:|---:|---:|---:|---:|
| 0 | 26 | 0 | 0 | 6 | — |
| 1 | 26 | 1 | 1 | 6 | 6 |
| 2 | 15 | 1 | 1 | 18 | 18 |
| **3–5** | **8** | **4** | **1** | **18** | **35** |
| 6 | 3 | 8 | 2 | 36 | 70 |
| 7–9 | 2 | 44 | 13 | 90 | 70 |

Level 0 still emits source COPY and RUN instructions. Levels 1–9 can also reuse earlier target data. The parser tries runs and source matches first. Positions that reach target matching enter a newest-first index. Long accepted matches skip their interiors. A bounded instruction buffer delays emission so a later source match can extend backwards and absorb previously proposed instructions. Hashing, source-position prediction, overlap resolution, and the VCDIFF writer are managed implementations. Patch size and speed depend on the data. The decoder needs only the patch and its source.

Two explicit search modes are also available:

- `VcdiffEncodeMode.Fast` uses eight-byte source landmarks and up to 256 collision hops. Its 32-bit source table stores a first-byte class mask in each link and stops searching when the remaining candidates cannot match the probe’s first byte. On windows with sampled source matches, it can look one byte ahead before committing a short COPY. It tests one predicted source address and can resume copying after an isolated byte edit. Fast emits source copies, literals, and runs.
- `VcdiffEncodeMode.Exhaustive` selects the former Default algorithm. It searches full source and target chains at each probe and ranks candidates by estimated wire cost.

All encoders split targets into 8 MiB windows. Source indexes cover up to 64 MiB and can be reused across windows. Indexed positions use 8-, 16-, or 32-bit entries, with stack scratch for small inputs and pooled storage for larger ones. `MemoryMode.LessMemory` uses the smaller power-of-two bucket count. `MemoryMode.Default` doubles it.

The writer packs adjacent instructions when the default table permits it and chooses among the standard address modes. It compares the complete window size with a single dictionary-free ADD, including the cost of declaring the source segment. Small windows use stack scratch and borrow the first literal span. The buffer-writer overload appends each completed window to the caller’s destination. The allocating overload creates an exact-sized result for single-window patches.

## Optional operation pass

`EncodeOptimized` runs an afterburner on each completed target window. It can extend a COPY or RUN through matching neighboring bytes, absorb redundant operations, and turn a short instruction into literals when that saves bytes. It also trims the source dictionary to the range used by source copies and rebases target-copy addresses.

```csharp
byte[] patch = Vcdiff.EncodeOptimized(source, target, VcdiffEncodeMode.Fast);
Vcdiff.EncodeOptimized(source, target, destinationWriter, compressionLevel: 9);
```

The pass re-encodes its candidate with fresh address caches and opcode pairing. It compares complete window sizes, including headers and the literal-only fallback. On a tie or a larger candidate, it emits the original bytes. This bounds patch size by the ordinary encode for the same mode, level, and memory policy. It uses temporary storage for one window’s operations and spends additional encoding time. The decoder uses the usual VCDIFF format. [Measured size and time tradeoffs](Results/Window-afterburner-net8-arm64.md)

## Benchmarks

Run the Release benchmark from the repository root.

```sh
dotnet run -c Release --project benchmarks/XDeltaSharp.Benchmarks
```

Optionally append two file paths after `--` to include a source/target pair. The harness checks the reconstructed bytes and reports median time per call, managed allocations per call, and patch size for both public output paths. The writer measurement reuses a preallocated destination. The recorded file pair uses DeltaSharp’s Moby-Dick editions, pg15.txt and pg2701.txt. [Previous full-chain Default and Fast on .NET 8 Arm64](Results/Default-vs-Fast-net8-arm64.csv) · [Fast memory mode comparison](Results/Fast-memory-modes-net8-arm64.csv) · [Original collision-chain baseline](Results/Fast-collision-chains-net8-arm64.csv) · [Earlier single-candidate baseline](Results/Fast-net8-arm64.csv)

Run `--levels <source> <target>` after the benchmark project’s `--` separator to measure each numeric level. [Moby-Dick level comparison with native xdelta3](Results/CompressionLevels-net8-arm64.csv)

The recorded comparison used the two repository Moby-Dick editions on an Apple M3 Max, .NET 8.0.25 Release. Both encoders used reusable output buffers and uncompressed sections. Each row reports the median of nine warmed batches of twelve in-memory encodes. The native harness used xdelta3 3.2.2 and its output was checked against the CLI at every level with checksums and application headers disabled.

The earlier [Moby-Dick encoder profile](Results/MobyDick-Default-profile.md) measured the algorithm now named Exhaustive. The `--profile-default` command measures the current level-3 default.

The [Fast lazy-matching comparison](Results/Fast-lazy-copy-net8-arm64.md) covers related files, isolated edits, and shuffled source chunks. Use `--fast-only <source> <target>` to measure Fast on your own pair.
