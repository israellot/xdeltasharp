# VCDiffSharp

A .NET 8 library for decoding VCDIFF patches. It supports RFC 3284 version 0 with the default code table and uncompressed sections. The encoder is currently a scaffold.

## Public API

```csharp
using VCDiffSharp;

byte[] restored = Vcdiff.Decode(source, patch, maxOutputBytes: 64 * 1024 * 1024);

// When the caller already owns the output buffer:
int bytesWritten = Vcdiff.Decode(source, patch, destination.AsSpan());
```

The allocating overload reads the window lengths, checks the cumulative output limit, and allocates the target once. Set `maxOutputBytes` to the largest target your application accepts. The span overload writes windows in order and may leave partial output on failure.

The decoder handles ADD, RUN, all nine COPY address modes, paired instructions, and overlapping copies inside the current window. Each window can select a dictionary from the source file or previously reconstructed target output. Pass an empty source for a patch that uses only target data.

Malformed patches and output above `maxOutputBytes` raise `InvalidDataException`. An undersized destination raises `ArgumentException`. Custom code tables, secondary compression, xdelta3 application headers and checksums, and open-vcdiff extension versions raise `NotSupportedException`.

## Structure

| Type | Responsibility |
| --- | --- |
| `Vcdiff` | Public entry points, file header, window sequence, and total output limit. |
| `Encoding/WindowEncoder` | Compare source COPY, target COPY, RUN, and literal costs. Emit the selected operations and frame the window. |
| `Encoding/SourceMatchFinder` | Search an indexed source segment. The encoder can reuse the index across windows that select that segment. |
| `Encoding/TargetMatchFinder` | Search earlier positions in the current target window. Each window starts with an empty index. |
| `Encoding/CopyMatch` | Carry a candidate’s read offset, target write offset, and length from either finder. |
| `Decoding/WindowReader` | Parse file and window headers, validate the dictionary range, and bound the three section cursors. |
| `Decoding/WindowDecoder` | Execute the data, instruction, and address sections against the selected dictionary and the target window being reconstructed. |
| `Format/AddressCache` | Maintain the per-window NEAR and SAME caches shared by the encoding and decoding algorithms. |
| `Format/DefaultCodeTable` | Describe the single and paired instructions represented by each of the 256 default opcodes. |

Run `dotnet test -c Release` for the [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284.html) vectors, malformed inputs, and [independent xdelta3 and open-vcdiff fixtures](tests/VCDiffSharp.Tests/Fixtures/README.md).

## How the encoder finds a COPY

At each probe position, `WindowEncoder` asks both finders for a candidate. The **source finder** is the porting boundary for DeltaSharp’s `Hashtable64` index, its `Match` / `More` candidate walk in `DeltaCreate.EncodeMatches`, and `SpanHelper.MatchForward` / `MatchBackward`. The index samples the source every eight bytes. A hit can extend backwards as far as the first pending literal. Returning the target write offset lets the encoder shorten that pending ADD before emitting the COPY.

The **target finder** advances its index as the probe moves, including positions passed over by a previous COPY or RUN. Only offsets before the probe are eligible as read starts. Because the encoder already has the target window, it can compare through the probe to discover overlapping matches. For example, at offset 4 in `ABCDABCDABCD`, the finder can return `CopyMatch(ReadOffset: 0, WriteOffset: 4, Length: 8)`. The encoder can then emit ADD(4) followed by COPY(8).

The finder that supplied the candidate determines its address space. A source COPY uses `ReadOffset` and a target COPY uses `SourceSegmentLength + ReadOffset`. The window encoder estimates savings against literal encoding, accounting for pending literals, opcode, size, and address bytes. It updates the address cache only when it commits a COPY. DeltaSharp’s fixed COPY threshold belongs to its original encoder and is omitted from the proposed finder contract.
