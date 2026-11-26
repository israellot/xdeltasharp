# XDeltaSharp

[![Line coverage](.github/badges/lines.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)
[![Branch coverage](.github/badges/branches.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)

XDeltaSharp creates binary patches and applies them on .NET 8. Give it the original bytes and the updated bytes to produce a VCDIFF patch. A recipient with the same original can reconstruct the update by applying the patch. The core library is managed C# and has no NuGet dependencies.

## Choose a package

| NuGet ID | Use it to | Entry point |
| --- | --- | --- |
| `xdelta` | Create patches and apply patches with uncompressed VCDIFF sections | `XDeltaSharp.Vcdiff` |
| `xdelta.Lzma` | Apply patches from xdelta3 builds that use LZMA secondary compression, including patches with mixed raw and compressed sections | `XDeltaSharp.Lzma.LzmaVcdiff` |
| `xdelta.Blake3` | Verify whole-file source and target digests in xdelta3 armor with either decoder | `XDeltaSharp.Blake3.Blake3Armor.Verify` |

Each optional package brings in `xdelta` automatically. Reference both add-ons to apply an LZMA patch and verify its armor.

## Create and apply a patch

```sh
dotnet add package xdelta
```

```csharp
using XDeltaSharp;

byte[] original = "Gate opens at nine."u8.ToArray();
byte[] updated  = "Gate opens at ten."u8.ToArray();

byte[] patch = Vcdiff.Encode(original, updated);
byte[] restored = Vcdiff.Decode(original, patch, maxOutputBytes: updated.Length);

Console.WriteLine(restored.AsSpan().SequenceEqual(updated)); // True
```

The encoded patch uses the RFC 3284 version 0 default code table. It can be applied by xdelta3. In the other direction, use `xdelta3 -e -S none -s original.bin updated.bin update.vcdiff` to create a patch for the core decoder. The default decoder skips the application header and checks every Adler-32 window checksum that is present. Custom code tables, secondary compressors other than the optional xdelta3 LZMA codec, and open-vcdiff extension versions are unsupported. [Format specification](https://www.rfc-editor.org/rfc/rfc3284.html) · [Interop fixtures](https://github.com/israellot/xdeltasharp/tree/master/tests/XDeltaSharp.Tests/Fixtures)

## Apply an xdelta3 patch to a file

For a default patch from an LZMA-enabled xdelta3 build, reference `xdelta.Lzma`. Add `xdelta.Blake3` to check any source and target digests embedded in the patch.

```csharp
using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;

using var source = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var output = File.Create("restored.bin");
long written = LzmaVcdiff.Decode(source, patch, output,
    armor: Blake3Armor.Verify, maxOutputBytes: 4L * 1024 * 1024 * 1024);
```

Without armor verification, omit the `armor` argument. With verification enabled, patches without recognized digests still decode. A source digest mismatch fails before output begins. A target digest mismatch fails after the complete target has been reconstructed, so treat the output as usable only after the call returns successfully. [xdelta3 armor format](https://github.com/jmacd/xdelta/blob/v3.2.2/site/docs/armor.md)

## Buffers, streams, and limits

| Output you need | API | Behavior |
| --- | --- | --- |
| A new target array | `Decode(source, patch, maxOutputBytes: limit)` | Checks the total target size before allocating the array. |
| A target in an existing buffer | `Decode(source, patch, buffer.AsSpan())` | Returns the byte count. Use `buffer.AsSpan(0, written)` for the reconstructed data. |
| A target file or a target larger than 2 GiB | `Decode(sourceStream, patchStream, outputStream)` | Reads patch windows sequentially and returns a 64-bit byte count. |
| Patch bytes appended to a writer | `Encode(source, target, writer)` | Appends each completed window to an `IBufferWriter<byte>`. |

`maxOutputBytes` limits total decoded bytes on the allocating and stream overloads. For a caller-owned span, its length limits the output. All decoders cap each decoded target window at **64 MiB**. Use `maxWindowBytes` to lower that limit independently. The encoder writes windows of up to **8 MiB**.

The patch stream only needs to be readable. Source copies require a readable, seekable source. Use `Stream.Null` for a dictionary-free patch. Windows that copy from previously decoded windows require a readable, writable, seekable destination; other windows can use a write-only destination. Offsets start at the streams’ initial positions. Decode leaves all streams open and restores a readable, seekable source to its initial position.

Malformed patches, integrity mismatches, and exceeded decode limits raise `InvalidDataException`. An undersized destination span raises `ArgumentException`. Span or stream output can contain reconstructed bytes when a call fails.

## Tune encoding

Start with `Vcdiff.Encode(source, target)`, which uses level 3. Pass `compressionLevel: 0` through `9` to vary the search effort. `VcdiffEncodeMode.Fast` uses a bounded source-only match search for related files. `VcdiffEncodeMode.Exhaustive` searches full source and target candidate chains. `MemoryMode.LessMemory` reduces match-index storage.

Use `EncodeOptimized` when smaller patches justify extra encoding work. It keeps a refined window only if it is smaller than the ordinary encoding for the same settings. The recipient uses the same decoder for every encoding mode.

## Development

The coverage badges summarize the core, LZMA, and BLAKE3 libraries together. Open a successful [coverage run](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml), download its `coverage-report` artifact, and open `index.html` for the per-file breakdown.

Run `dotnet test XDeltaSharp.sln -c Release` from the repository root. The suite includes real xdelta3 and open-vcdiff fixtures. For matcher settings, implementation details, benchmark commands, and measurements, see the [encoder and benchmark notes](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md).
