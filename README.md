# XDeltaSharp

[![NuGet version](https://img.shields.io/nuget/v/xdelta)](https://www.nuget.org/packages/xdelta)
[![NuGet downloads](https://img.shields.io/nuget/dt/xdelta)](https://www.nuget.org/packages/xdelta)
[![CI status](https://img.shields.io/github/actions/workflow/status/israellot/xdeltasharp/coverage.yml?branch=master&label=CI)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml)
[![Line coverage](https://raw.githubusercontent.com/israellot/xdeltasharp/master/.github/badges/lines.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)
[![License](https://img.shields.io/badge/license-MIT-blue)](https://github.com/israellot/xdeltasharp/blob/master/LICENSE)

XDeltaSharp creates and applies **VCDIFF binary patches in managed C#**. For example, a game launcher can download a patch and reconstruct an updated asset bundle from the version already on disk. The core NuGet package has no dependencies and ships matching builds for .NET 8, 9, 10, and 11.

For two editions of *Moby-Dick*, XDeltaSharp reduced a **1.25 MB update to a 21.3 kB patch** (1.7% of the updated file). On an Apple M1 it encoded that pair **2.43× as fast as native xdelta3** at compression level 3. [Benchmark results](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/Results/MobyDick-xdelta3-net11.csv)

- Exchange patches with the xdelta3 CLI using the standard VCDIFF code table and uncompressed sections.
- Apply a patch window by window from a stream, with limits on buffering and total output.
- Write patches to an `IBufferWriter<byte>` and decode into a buffer you already own.

## Create and apply your first patch

```sh
dotnet add package xdelta
```

The sender needs both versions to create the patch. The recipient needs the patch and the same original bytes.

```csharp
using XDeltaSharp;

var original = "The quick brown fox jumps over the lazy dog."u8;
var updated  = "The quick brown hare jumps over the lazy dog."u8;

byte[] patch = Vcdiff.Encode(original, updated);
byte[] restored = Vcdiff.Decode(original, patch);

Console.WriteLine(updated.SequenceEqual(restored)); // True
```

## Performance against native xdelta3

These are median times per operation on the *Moby-Dick* pair at level 3. XDeltaSharp produced a 21,287-byte patch and xdelta3 produced a 21,534-byte patch. To compare decoding on identical input, both decoders applied the xdelta3 patch.

| Host | XDeltaSharp encode | xdelta3 encode | Encode speedup | XDeltaSharp decode | xdelta3 decode |
| --- | ---: | ---: | ---: | ---: | ---: |
| Apple M1 · macOS 15 | 2.29 ms | 5.58 ms | **2.43×** | 0.21 ms | 0.22 ms |
| Intel Xeon 6975P-C · Ubuntu 24.04 | 2.55 ms | 3.47 ms | **1.36×** | 0.20 ms | 0.22 ms |
| AMD EPYC 9V74 · Ubuntu 22.04 | 2.33 ms | 3.31 ms | **1.42×** | 0.17 ms | 0.15 ms |

The benchmark runs .NET 11 and native xdelta3 3.2.2 in the same process, with inputs in memory and reusable output buffers. It takes the median of seven warmed batches and verifies that both decoders reconstruct the updated file byte for byte. [Reproduce the comparison](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md#reproduce-the-readme-comparison) · [Full results for levels 0–9 (CSV)](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/Results/MobyDick-xdelta3-net11.csv)

Other benchmarks cover [encoding SQLite binaries](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/Results/Level-SIMD-net8-arm64.md) and [decoder allocations for array, span, and stream output](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md#decoder-benchmarks).

## Apply an update to a file

```csharp
using XDeltaSharp;

using var original = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");

Vcdiff.Decode(original, patch, restored);
```

The decoder reads the patch sequentially and leaves all three streams open. When a patch copies from the original, the source must be readable and seekable. When it copies from a previous output window, the destination must also support reading and seeking.

A failed decode can leave a partially written file. When installing an update, decode to a temporary file, verify it against the expected digest from your release manifest, then replace the installed file.

### Set decoding limits

When accepting downloaded patches, choose limits that fit the files you expect. In the file example, replace the `Vcdiff.Decode` call with the following to cap the encoded payload, the decoded window, and the entire update independently.

```csharp
const int MiB = 1024 * 1024;
Vcdiff.DecodeWithLimits(original, patch, restored,
    maxEncodedWindowBytes: 4 * MiB,
    maxWindowBytes: 8 * MiB,
    maxOutputBytes: 64 * MiB);
```

By default, stream decoding allows up to `Array.MaxLength` encoded bytes per window, 64 MiB per decoded window, and `long.MaxValue` total output bytes. For an in-memory result, use `Vcdiff.Decode(original, patch, maxOutputBytes: 64 * 1024 * 1024)` to set a 64 MiB total limit. The array overload defaults its `maxOutputBytes` parameter to `int.MaxValue` and also enforces `Array.MaxLength`.

## Use your own buffers

The encoder can append to any `IBufferWriter<byte>`. If you know the updated file size, supply a destination span to the decoder and use the returned byte count to read the result.

```csharp
using System.Buffers;
using XDeltaSharp;

var original = File.ReadAllBytes("original.bin");
var updated = File.ReadAllBytes("updated.bin");

var patch = new ArrayBufferWriter<byte>();
Vcdiff.Encode(original, updated, patch);

var buffer = new byte[updated.Length];
int written = Vcdiff.Decode(original, patch.WrittenSpan, buffer.AsSpan());
ReadOnlySpan<byte> restored = buffer.AsSpan(0, written);
```

## Tune the encoder

| Goal | Call |
| --- | --- |
| Search more thoroughly for matches (levels 0–9, default 3) | `Vcdiff.Encode(original, updated, compressionLevel: 9)` |
| Spend extra encoding time to try for a smaller patch | `Vcdiff.Encode(original, updated, optimize: true)` |
| Halve the number of buckets in the source index | `Vcdiff.Encode(original, updated, MemoryMode.LessMemory)` |

## Exchange patches with xdelta3

The core package reads and writes uncompressed VCDIFF sections using the default code table. To create a patch that the core decoder can read, disable secondary compression in xdelta3 with `-S none`. [VCDIFF specification (RFC 3284)](https://www.rfc-editor.org/rfc/rfc3284.html)

```sh
# Create a patch for XDeltaSharp with xdelta3
xdelta3 -e -S none -s original.bin updated.bin update.vcdiff

# Apply an XDeltaSharp patch with xdelta3
xdelta3 -d -s original.bin update.vcdiff restored.bin
```

| If your patch uses… | Install | Use |
| --- | --- | --- |
| LZMA secondary compression | [xdelta.Lzma](https://www.nuget.org/packages/xdelta.Lzma) | `LzmaVcdiff.Decode(original, patch)` |
| Embedded BLAKE3 digests | [xdelta.Blake3](https://www.nuget.org/packages/xdelta.Blake3) | `Vcdiff.Decode(original, patch, armor: Blake3Armor.Verify)` |

## Testing and contributing

CI runs the test suite on Windows and Linux, and checks interoperability with the xdelta3 CLI on Linux. Native AOT, trimming, and Unity are untested. Report bugs or ask questions in [GitHub Issues](https://github.com/israellot/xdeltasharp/issues). For local setup, fixture details, and instructions for running the package examples, see the [contributing guide](https://github.com/israellot/xdeltasharp/blob/master/CONTRIBUTING.md).

## License

XDeltaSharp is available under the [MIT license](https://github.com/israellot/xdeltasharp/blob/master/LICENSE).
