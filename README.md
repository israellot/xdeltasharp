# XDeltaSharp

[![Line coverage](.github/badges/lines.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)
[![Branch coverage](.github/badges/branches.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)

XDeltaSharp creates and applies VCDIFF binary patches in .NET 8 and later. If someone already has the original file, you can send them a patch to reconstruct the updated file. The core library is written in C# and has no package dependencies.

## Create and apply a patch

Install the core package in your .NET project.

```sh
dotnet add package xdelta
```

`Encode` takes the original and updated bytes and returns a patch. On the receiving end, `Decode` needs only the original bytes and that patch.

```csharp
using XDeltaSharp;

byte[] original = File.ReadAllBytes("original.bin");
byte[] updated = File.ReadAllBytes("updated.bin");

byte[] patch = Vcdiff.Encode(original, updated);
byte[] restored = Vcdiff.Decode(original, patch); // Same bytes as updated
```

Use `File.WriteAllBytes("update.vcdiff", patch)` to save the patch for distribution.

## Apply a patch to a file

To reconstruct a file on disk, pass three streams to `Decode`. This lets you read the patch and write the result one window at a time.

```csharp
using XDeltaSharp;

using var original = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");

Vcdiff.Decode(original, patch, restored);
```

Supply the same original file used to create the patch. Choose a separate path for the restored file because `File.Create` overwrites an existing file.

## Use patches with xdelta3

Apply a patch created by XDeltaSharp with the xdelta3 command-line tool.

```sh
xdelta3 -d -s original.bin update.vcdiff restored.bin
```

To go the other direction, use `-S none` when creating the patch with xdelta3. This disables secondary compression so the core package can apply it.

```sh
xdelta3 -e -S none -s original.bin updated.bin update.vcdiff
```

The core package supports RFC 3284 version 0 patches with the default code table and uncompressed sections, including standard open-vcdiff output. Both the core and LZMA decoders verify Adler-32 window checksums when present. [VCDIFF specification](https://www.rfc-editor.org/rfc/rfc3284.html)

### Optional packages

- **LZMA compression.** If you receive an LZMA-compressed xdelta3 patch, install `xdelta.Lzma` and use `LzmaVcdiff.Decode(original, patch, restored)`. [NuGet package](https://www.nuget.org/packages/xdelta.Lzma) · [Usage and memory limits](https://github.com/israellot/xdeltasharp/blob/master/src/XDeltaSharp.Lzma/README.md)
- **BLAKE3 verification.** If the patch includes whole-file digests, `xdelta.Blake3` can verify the original and restored files with either decoder. Use the restored file only after decoding succeeds, since the target digest is checked at the end. [NuGet package](https://www.nuget.org/packages/xdelta.Blake3) · [Verification options](https://github.com/israellot/xdeltasharp/blob/master/src/XDeltaSharp.Blake3/README.md)

## Patch size and encoding options

For two editions of *Moby-Dick*, the default encoder produced a **21,287-byte patch**, or **1.7%** of the 1,253,974-byte updated file. Patch size depends on how much data the encoder can reuse from the original. [Sample files](https://github.com/israellot/DeltaSharp/tree/main/src/Benchmarks/Data)

`Vcdiff.Encode` defaults to compression level 3. Pass `compressionLevel: 0` through `9` to tune how much time the encoder spends searching for matches. Use `Vcdiff.EncodeOptimized` for an additional pass to reduce patch size, or `MemoryMode.LessMemory` to halve the memory used by the source index. The same decoder handles all levels. [Encoding levels, memory use, and benchmarks](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md#encoding-levels)

## Buffers and decoding limits

- To reuse an output buffer, pass a `Span<byte>` to `Decode`. It returns the number of bytes written. To encode into a buffer writer, pass an `IBufferWriter<byte>` to `Encode`.
- For stream decoding, the patch is read sequentially. Copies from the original require a readable, seekable source. Copies from earlier output also require a readable, seekable destination. The caller owns all three streams.
- To cap the total output, pass `maxOutputBytes` to `Decode`. Use `maxWindowBytes` to lower the default 64 MiB decoded-window limit. For stream input, `DecodeWithLimits` can also cap the encoded bytes buffered for each window.

Released under the [MIT License](https://opensource.org/license/mit).
