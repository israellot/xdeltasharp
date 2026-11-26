# XDeltaSharp

[![Line coverage](https://raw.githubusercontent.com/israellot/xdeltasharp/master/.github/badges/lines.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)
[![Branch coverage](https://raw.githubusercontent.com/israellot/xdeltasharp/master/.github/badges/branches.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)

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
long written = LzmaVcdiff.DecodeWithLimits(source, patch, output,
    maxEncodedWindowBytes: 8 * 1024 * 1024,
    maxOutputBytes: 4L * 1024 * 1024 * 1024,
    armor: Blake3Armor.Verify);
```

Without armor verification, omit the `armor` argument. With verification enabled, patches without recognized digests still decode. A source digest mismatch fails before output begins. A target digest mismatch fails after the complete target has been reconstructed, so treat the output as usable only after the call returns successfully. [xdelta3 armor format](https://github.com/jmacd/xdelta/blob/v3.2.2/site/docs/armor.md)

## Buffers, streams, and limits

| Output you need | API | Behavior |
| --- | --- | --- |
| A new target array | `Decode(source, patch, maxOutputBytes: limit)` | Checks the total target size before allocating the array. |
| A target in an existing buffer | `Decode(source, patch, buffer.AsSpan())` | Returns the byte count. Use `buffer.AsSpan(0, written)` for the reconstructed data. |
| A target file or a target larger than 2 GiB | `Decode(sourceStream, patchStream, outputStream)` | Reads patch windows sequentially and returns a 64-bit byte count. |
| Patch bytes appended to a writer | `Encode(source, target, writer)` | Appends each completed window to an `IBufferWriter<byte>`. |

`maxOutputBytes` limits total decoded bytes on the allocating and stream overloads. For a caller-owned span, its length limits the output. All decoders cap each decoded target window at **64 MiB**. For example, `Vcdiff.Decode(source, patch, maxWindowBytes: limit)` lowers just the decoded window limit on an allocating call. The encoder writes windows of up to **8 MiB**.

The destination span can overlap the source or the patch. The decoder copies each overlapping input in full before writing. Use disjoint buffers to avoid those copies.

The patch stream only needs to be readable. Source copies require a readable, seekable source. Use `Stream.Null` for a dictionary-free patch. Windows that copy from previously decoded windows require a readable, writable, seekable destination. Other windows can use a write-only destination. Use independent streams for the three roles. Offsets start at the streams’ initial positions. Decode leaves all streams open and restores a readable, seekable source to its initial position.

Stream decoding buffers one window’s encoded data, instructions, and addresses. Use `Vcdiff.DecodeWithLimits` or `LzmaVcdiff.DecodeWithLimits` to cap their combined length with `maxEncodedWindowBytes`. The decoder checks the declared lengths before reading the payload. The file example above allows 8 MiB per encoded window and 4 GiB of total output. Framing, decoded output, and LZMA working memory have separate costs. Existing `Decode` calls allow up to `Array.MaxLength` encoded payload bytes per window.

Malformed patches, integrity mismatches, and exceeded decode limits raise `InvalidDataException`. An undersized destination span raises `ArgumentException`. Span or stream output can contain reconstructed bytes when a call fails.

## Tune encoding

Both `Encode` and `EncodeOptimized` use level 3 by default. Pass a `compressionLevel` from 0 through 9 to select an xdelta3-derived search preset. `MemoryMode.LessMemory` reduces match-index storage.

Use `EncodeOptimized` when smaller patches justify extra encoding work. It keeps a refined window only if it is smaller than the ordinary encoding for the same settings. Patches from every level use the same decoder.

## Development

The coverage badges summarize the core, LZMA, and BLAKE3 libraries together. Open a successful [coverage run](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml), download its `coverage-report` artifact, and open `index.html` for the per-file breakdown.

The libraries target .NET 8 and use C# 12. Release CI uses the exact SDK pinned in the build workflow. Local builds prefer that SDK and can use a newer stable SDK when needed. Run `dotnet test XDeltaSharp.sln -c Release` from the repository root. The suite includes real xdelta3 and open-vcdiff fixtures. For matcher settings, implementation details, benchmark commands, and measurements, see the [encoder and benchmark notes](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md).

Run `dotnet format whitespace XDeltaSharp.sln --verify-no-changes` to check formatting. The build compares the three libraries to their `PublicAPI.Shipped.txt` baselines. Record intentional additions in the corresponding `PublicAPI.Unshipped.txt` file.

Run `python3 eng/test-packages.py` to build the three NuGet packages and their symbols, then compile and run the examples above against a fresh local feed. The file example uses the checked-in multi-window xdelta3 fixture with LZMA compression and BLAKE3 armor.

## Publishing to NuGet.org

All three packages share the `VersionPrefix` in `src/Directory.Build.props`. The first release is **1.0.0**. Pushing the matching Git tag `v1.0.0` runs the tests, builds the three packages and their symbols, verifies the packaged examples, and publishes those exact artifacts. For subsequent releases, update the shared version before tagging the release commit. Running **Publish NuGet packages** manually builds and verifies downloadable artifacts.

Before the first release, create a NuGet.org trusted publishing policy under the `israellot` profile for the GitHub repository `israellot/xdeltasharp`, workflow filename `publish.yml`, and environment `nuget`. Allow new packages and new versions for `xdelta`, `xdelta.Lzma`, and `xdelta.Blake3`. GitHub Actions exchanges its OIDC identity for a short-lived publishing key on each tagged release. [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
