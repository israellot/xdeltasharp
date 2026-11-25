# VCDiffSharp

A .NET 8 library for creating and applying RFC 3284 version 0 patches with the default code table and uncompressed sections.

## Public API

```csharp
using VCDiffSharp;

byte[] patch = Vcdiff.Encode(source, target, VcdiffEncodeMode.Fast);
byte[] restored = Vcdiff.Decode(source, patch, maxOutputBytes: 256 * 1024 * 1024);

// When the caller already owns the output buffer:
int bytesWritten = Vcdiff.Decode(source, patch, destination.AsSpan());

// File or stream inputs, including targets larger than 2 GiB:
long totalWritten = Vcdiff.Decode(sourceStream, patchStream, outputStream);
```

The allocating overload reads the window lengths, checks the cumulative output limit, and allocates the target once. Set `maxOutputBytes` to the largest target your application accepts. The span overload writes windows in order and may leave partial output on failure.

All decode overloads enforce a **64 MiB maximum decoded target window**, inclusive, matching [xdelta3’s default hard limit](https://github.com/jmacd/xdelta/blob/v3.2.2/xdelta3/xdelta3.h). Use `maxWindowBytes` to request a smaller limit. The limit applies independently to every window. The encoder uses xdelta3’s **8 MiB default target window**.

The stream overload reads the patch sequentially and uses 64-bit file offsets and cumulative output counts. It buffers the current decoded window and its encoded sections (which must fit in one .NET byte array), and reads dictionary COPY ranges on demand. For external dictionaries, supply a readable, seekable source. For windows referencing earlier target output, open the destination for reading, writing, and seeking. Other windows can decode to a write-only stream. All offsets start at the streams’ initial positions, and the streams remain open. Use `Stream.Null` as the source for dictionary-free patches.

The decoder handles ADD, RUN, all nine COPY address modes, paired instructions, and overlapping copies inside the current window. Each window can select a dictionary from the source file or previously reconstructed target output. Pass an empty source for a patch that uses only target data.

Malformed patches and output above `maxOutputBytes` raise `InvalidDataException`. An undersized destination raises `ArgumentException`. Custom code tables, secondary compression, xdelta3 application headers and checksums, and open-vcdiff extension versions raise `NotSupportedException`.

## Structure

| Type | Responsibility |
| --- | --- |
| `Vcdiff` | Public entry points, file header, window sequence, and total output limit. |
| `Encoding/WindowEncoder` | Select source segments and scan target windows for source matches and runs. |
| `Encoding/WindowWriter` | Frame the window and write its data, instructions, and COPY addresses. |
| `Encoding/SourceMatchFinder` | Search an indexed source segment. The encoder can reuse the index across windows that select that segment. |
| `Encoding/TargetMatchFinder` | Reserved for the planned Default mode. |
| `Encoding/CopyMatch` | Carry a candidate’s read offset, target write offset, and length from either finder. |
| `Decoding/WindowReader` | Parse file and window headers, validate the dictionary range, and bound the three section cursors. |
| `Decoding/WindowDecoder` | Execute the data, instruction, and address sections against the selected dictionary and the target window being reconstructed. |
| `Format/AddressCache` | Resolve COPY addresses during decoding using per-window NEAR and SAME caches. |
| `Format/DefaultCodeTable` | Describe the single and paired instructions represented by each of the 256 default opcodes. |

Run `dotnet test -c Release` for the [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284.html) vectors, malformed inputs, and [independent xdelta3 and open-vcdiff fixtures](tests/VCDiffSharp.Tests/Fixtures/README.md).

## Fast encoding

`VcdiffEncodeMode.Fast` searches one source candidate per probe and recognizes runs of repeated bytes. Both mode-less Encode overloads currently use Fast. Specify Fast explicitly to keep that policy when the planned Default mode adds target matching.

The source matcher uses DeltaSharp’s eight-byte landmarks and compact, one-based block indices. For a tiny target it can probe a four-byte seed directly in a small source or skip a disproportionately large source index. For indexed sources through 2,047 bytes, bucket heads fit inline. Larger tables rent 16- or 32-bit entries. A source index covers up to 64 MiB and serves consecutive 8 MiB target windows selecting the same segment. Matches can extend backward into pending literals.

The writer packs adjacent instructions when the default table permits it and chooses among the standard address modes. It compares the complete window size with a single dictionary-free ADD, including the cost of declaring the source segment. Small windows use stack scratch and borrow the first literal span. The buffer-writer overload appends each completed window to the caller’s destination. The allocating overload creates an exact-sized result for single-window patches.

## Benchmarks

Run the Release benchmark from the repository root.

```sh
dotnet run -c Release --project benchmarks/VCDiffSharp.Benchmarks
```

Optionally append two file paths after `--` to include a source/target pair. The harness checks the reconstructed bytes and reports median time per call, managed allocations per call, and patch size for both public output paths. The writer measurement reuses a preallocated destination. The recorded file pair uses DeltaSharp’s Moby-Dick editions, pg15.txt and pg2701.txt. [Fast baseline on .NET 8 Arm64](benchmarks/Results/Fast-net8-arm64.csv)
