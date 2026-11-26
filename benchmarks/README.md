# Encoder and benchmark notes

## Implementation map

| Type | Responsibility |
| --- | --- |
| `Vcdiff` | Public entry points for encoding and decoding. |
| `Encoding/WindowEncoder` | Split targets into windows and reuse the source index across windows that share a source segment. |
| `Encoding/LevelParser` | Apply the numbered matcher profile and resolve pending instructions before writing. |
| `Encoding/WindowWriter` | Frame the window and write its data, instructions, and COPY addresses. |
| `Encoding/LevelSourceMatcher` | Search source landmarks sampled at the selected level’s stride. |
| `Encoding/LevelTargetMatcher` | Search earlier positions in the current target window within the selected level’s candidate limit. |
| `Encoding/CopyMatch` | Carry a candidate’s read offset, target write offset, and length from either finder. |
| `Decoding/WindowReader` | Parse file and window headers, validate the dictionary range, and bound the three section cursors. |
| `Decoding/WindowDecoder` | Execute the data, instruction, and address sections against the selected dictionary and the target window being reconstructed. |
| `Format/AddressCache` | Resolve COPY addresses during decoding using per-window NEAR and SAME caches. |
| `Format/DefaultCodeTable` | Describe the single and paired instructions represented by each of the 256 default opcodes. |

Run `dotnet test -c Release` for the [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284.html) vectors, malformed inputs, and [independent xdelta3 and open-vcdiff fixtures](../tests/XDeltaSharp.Tests/Fixtures/README.md).

## Encoding levels

Both allocating and buffer-writer APIs accept an integer compression level from **0 to 9** and an optional memory policy. Omitting the level uses **level 3**.

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

The encoder splits targets into 8 MiB windows. Source indexes cover up to 64 MiB and can be reused across windows. Indexed positions use 8-, 16-, or 32-bit entries, with stack scratch for small inputs and pooled storage for larger ones. `MemoryMode.LessMemory` uses the smaller power-of-two bucket count. `MemoryMode.Default` doubles it.

The writer packs adjacent instructions when the default table permits it and chooses among the standard address modes. It compares the complete window size with a single dictionary-free ADD, including the cost of declaring the source segment. Small windows use stack scratch and borrow the first literal span. The buffer-writer overload appends each completed window to the caller’s destination. The allocating overload creates an exact-sized result for single-window patches.

## Optional operation pass

`EncodeOptimized` runs an afterburner on each completed target window. It can extend a COPY or RUN through matching neighboring bytes, absorb redundant operations, and turn a short instruction into literals when that saves bytes. It also trims the source dictionary to the range used by source copies and rebases target-copy addresses.

```csharp
byte[] patch = Vcdiff.EncodeOptimized(source, target);
Vcdiff.EncodeOptimized(source, target, destinationWriter, compressionLevel: 9);
```

The pass re-encodes its candidate with fresh address caches and opcode pairing. It compares complete window sizes, including headers and the literal-only fallback. On a tie or a larger candidate, it emits the original bytes. This bounds patch size by the ordinary encode for the same level and memory policy. It uses temporary storage for one window’s operations and spends additional encoding time. The decoder uses the usual VCDIFF format.

## Benchmarks

Run the Release benchmark from the repository root.

```sh
dotnet run -c Release --project benchmarks/XDeltaSharp.Benchmarks
```

Optionally append two file paths after `--` to include a source/target pair. The default run compares both memory policies at level 3 and reports median time per call, managed allocations per call, and patch size for the array and buffer-writer APIs. The writer measurement reuses a preallocated destination. The harness checks that both APIs produce the same patch and that decoding restores the target bytes.

Run `--levels <source> <target>` after the benchmark project’s `--` separator to measure each numeric level. [Moby-Dick level comparison with native xdelta3](Results/CompressionLevels-net8-arm64.csv)

The recorded comparison used the two repository Moby-Dick editions on an Apple M3 Max, .NET 8.0.25 Release. Both encoders used reusable output buffers and uncompressed sections. Each row reports the median of nine warmed batches of twelve in-memory encodes. The native harness used xdelta3 3.2.2 and its output was checked against the CLI at every level with checksums and application headers disabled.

Use `--profile-default <source> <target>` to measure the level-3 default and print its patch hash. Use `--afterburner <source> <target>` to compare ordinary and optimized output at each level.

Use `--short-search` to time an unrelated 256-byte target and an exact match at the end of a 64 MiB source. When a short target would need a disproportionately large source index, the encoder spaces up to eight full source scans across the target. At every probe it still checks the predicted source position. A match at a sampled probe can extend backwards. Short moved fragments between probes can become literals.

Historical results for the retired Fast and Exhaustive matchers remain in `Results/`, including the [original Default profile](Results/MobyDick-Default-profile.md). The [afterburner comparison](Results/Window-afterburner-net8-arm64.md) records all three implementations at the time of that experiment. Its Default rows refer to level 3.

## Decoder benchmarks

Run the array, span, and stream decoders on a seven-byte literal, the xdelta3 checksum fixture, and the three-window xdelta3 LZMA fixture. The two raw patches run through the core and optional LZMA decoders to measure per-call overhead. The compressed fixture runs with and without BLAKE3 verification.

```sh
dotnet run -c Release --project benchmarks/XDeltaSharp.Benchmarks -- --decode
```

The harness reuses the span buffer and memory streams, and checks the length and SHA-256 of every output path outside the timed batches. Each row reports the median time across seven warmed batches and average managed bytes allocated on the calling thread. The recorded run used an Apple M3 Max, .NET 8.0.34 Arm64 and SDK 8.0.428 in Release. [Decoder measurements](Results/Decoder-net8-arm64.csv)

The decoder benchmark also reorders a 1 MiB source in 32-byte records. One case shuffles within 4 KiB groups, and the other shuffles across the whole file. The `FileStream source` rows read a temporary file with a warm operating-system cache. Patch input and output stay in memory. Repeated small COPYs within a 4 KiB region share a decoder read, while widely scattered COPYs generally require individual reads.

The following medians compare `ff18cc2` with the bounded short-target search and file dictionary cache. Both builds used .NET 8.0.34 Arm64, SDK 8.0.428, Release on an Apple M3 Max, with `DOTNET_TieredCompilation=0`. Encoding used level 3 and the default memory policy.

| Workload | Patch bytes | Before | After |
| --- | ---: | ---: | ---: |
| Encode unrelated 256-byte target against 64 MiB, array API | 274 | 504 ms | 15.7 ms |
| Encode 256-byte match at end of 64 MiB, array API | 23 | 2.04 ms | 1.94 ms |
| Decode clustered 32-byte records from file | 150,506 | 1.95 ms | 0.94 ms |
| Decode scattered 32-byte records from file | 178,303 | 14.77 ms | 14.61 ms |
