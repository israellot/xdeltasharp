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

## Benchmarks against xdelta3

How long does it take to create and apply the *Moby-Dick* update? At level 3, XDeltaSharp produced a **21,287-byte** patch and native xdelta3 3.2.2 produced **21,534 bytes**. For the decode comparison, both libraries applied the same 21,534-byte xdelta3 patch and reconstructed the 1,253,974-byte target.

Median milliseconds per operation on GitHub-hosted runners, using .NET 8.0.34 and native Release builds. Inputs and output buffers were already in memory. Lower is faster.

| Processor | OS | XDeltaSharp encode | xdelta3 encode | XDeltaSharp decode | xdelta3 decode |
| --- | --- | ---: | ---: | ---: | ---: |
| Apple M1 (Arm64) | macOS 15.8.2 | 4.713 | 6.539 | 0.251 | 0.285 |
| Intel Core i7-8700B (x64) | macOS 15.7.9 | 8.882 | 10.089 | 0.437 | 0.474 |
| Intel Xeon 6975P-C (x64) | Ubuntu 24.04 | 2.873 | 3.414 | 0.233 | 0.206 |
| AMD EPYC 7763 (x64) | Ubuntu 22.04 | 3.914 | 4.098 | 0.275 | 0.229 |

XDeltaSharp encoded this update faster on all four hosts. Decode times favored XDeltaSharp on the M1 and Core i7, and xdelta3 on the Xeon and EPYC. [All ten levels and both patch origins (CSV)](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/Results/MobyDick-xdelta3-net8.csv) · [Method and reproduction steps](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md#reproduce-the-readme-comparison) · [Benchmark run](https://github.com/israellot/xdeltasharp/actions/runs/45991937854)

## Buffers and decoding limits

- To reuse an output buffer, pass a `Span<byte>` to `Decode`. It returns the number of bytes written. To encode into a buffer writer, pass an `IBufferWriter<byte>` to `Encode`.
- For stream decoding, the patch is read sequentially. Copies from the original require a readable, seekable source. Copies from earlier output also require a readable, seekable destination. The caller owns all three streams.
- To cap the total output, pass `maxOutputBytes` to `Decode`. Use `maxWindowBytes` to lower the default 64 MiB decoded-window limit. For stream input, `DecodeWithLimits` can also cap the encoded bytes buffered for each window.

Released under the [MIT License](https://opensource.org/license/mit).
