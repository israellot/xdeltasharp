# XDeltaSharp

XDeltaSharp creates and applies VCDIFF binary patches in .NET 8 and later. Use it to distribute an update when the recipient already has the original file. The patch can reuse byte ranges from the original, even when those bytes have moved. The core library is managed C# with no package dependencies.

For example, the default encoder produces a **21,287-byte patch** between two editions of *Moby-Dick*. With the original file already on disk, the download is **1.7% of the 1,253,974-byte updated file**. [Sample files](https://github.com/israellot/DeltaSharp/tree/main/src/Benchmarks/Data)

## Install and try it

```sh
dotnet add package xdelta
```

```csharp
using XDeltaSharp;

byte[] original = "color=blue\nwidth=1200\nheight=800\n"u8.ToArray();
byte[] updated  = "color=blue\nwidth=1280\nheight=800\n"u8.ToArray();

byte[] patch = Vcdiff.Encode(original, updated);
byte[] restored = Vcdiff.Decode(original, patch);

Console.WriteLine(restored.AsSpan().SequenceEqual(updated)); // True
```

Encoding defaults to level 3. Pass `compressionLevel: 0` through `9` to adjust the search effort, or use `EncodeOptimized` to spend extra encoding time looking for a smaller patch. The decoder works with every level.

## Using patches with xdelta3

The core encoder writes RFC 3284 version 0 patches using the default code table and uncompressed sections. When creating a patch with the xdelta3 CLI, use `-S none` to disable secondary compression.

```sh
# Apply a patch produced by XDeltaSharp.
xdelta3 -d -s original.bin update.vcdiff restored.bin

# Create a patch that Vcdiff.Decode can apply.
xdelta3 -e -S none -s original.bin updated.bin update.vcdiff
```

| NuGet package | When to use it |
| --- | --- |
| [xdelta](https://www.nuget.org/packages/xdelta) | Create patches and apply patches with uncompressed sections and the default code table, including standard-format open-vcdiff output. |
| [xdelta.Lzma](https://www.nuget.org/packages/xdelta.Lzma) | Apply xdelta3 patches with LZMA-compressed sections. The same decoder also accepts uncompressed sections. |
| [xdelta.Blake3](https://www.nuget.org/packages/xdelta.Blake3) | Verify whole-file source and target digests when an xdelta3 patch contains BLAKE3 armor. Works with either decoder. |

Each add-on includes the core package as a dependency. Both decoders verify Adler-32 window checksums when present. Custom code tables and open-vcdiff extension formats are unsupported. [VCDIFF specification](https://www.rfc-editor.org/rfc/rfc3284.html)

## Apply a patch to a file

Install both add-ons to read an LZMA-compressed patch and verify its embedded digests. This example caps each window’s encoded payload at 8 MiB and the total reconstructed file at 4 GiB.

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

A source digest mismatch fails before writing. A target digest mismatch is detected after reconstruction, so use the output only after the call succeeds. Patches without recognized digests still decode.

## Buffers, streams, and limits

- **Caller-owned buffers.** Pass a `Span<byte>` to `Decode` to fill an existing buffer. The return value is the number of bytes written. To append an encoded patch to a buffer writer, pass an `IBufferWriter<byte>` to `Encode`.
- **Streams.** The patch is read sequentially. Source copies require a readable, seekable source. A patch that copies from earlier output also needs a readable, seekable destination. Decode leaves the streams open.
- **Limits.** `maxOutputBytes` caps total decoded output. `maxWindowBytes` lowers the default 64 MiB decoded-window limit. For stream input, `DecodeWithLimits` also checks the combined encoded section sizes before buffering each window.

Released under the [MIT License](https://opensource.org/license/mit).
