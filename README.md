# XDeltaSharp

[![Line coverage](.github/badges/lines.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)
[![Branch coverage](.github/badges/branches.svg)](https://github.com/israellot/xdeltasharp/actions/workflows/coverage.yml?query=branch%3Amaster+event%3Apush)

XDeltaSharp creates VCDIFF binary patches in .NET 8+. Send a patch to anyone who has the original file so they can reconstruct the update. The core package is managed C# with no dependencies.

```sh
dotnet add package xdelta
```

## Create and apply a patch

```csharp
using System.Text;
using XDeltaSharp;

var original = Encoding.UTF8.GetBytes("port=8080");
var updated = Encoding.UTF8.GetBytes("port=8081");

byte[] patch = Vcdiff.Encode(original, updated);
byte[] restored = Vcdiff.Decode(original, patch);

Console.WriteLine(Encoding.UTF8.GetString(restored)); // port=8081
```

## Apply a patch to a file

```csharp
using XDeltaSharp;

using var original = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");

Vcdiff.Decode(original, patch, restored);
```

The decoder reads the patch one window at a time. Copies from the original file require a seekable source. Copies from earlier output require a readable, seekable destination.

## Encoding and decoding options

| Goal | Example |
| --- | --- |
| Search harder for matches (levels 0–9, default 3) | `Vcdiff.Encode(original, updated, compressionLevel: 9)` |
| Try an extra pass for a smaller patch | `Vcdiff.EncodeOptimized(original, updated)` |
| Halve the source index size | `Vcdiff.Encode(original, updated, MemoryMode.LessMemory)` |
| Limit decoded output to 64 MiB | `Vcdiff.Decode(original, patch, maxOutputBytes: 64 * 1024 * 1024)` |

## Use patches with xdelta3

```sh
# Apply an XDeltaSharp patch
xdelta3 -d -s original.bin update.vcdiff restored.bin

# Create a patch for XDeltaSharp's core decoder
xdelta3 -e -S none -s original.bin updated.bin update.vcdiff
```

The core decoder reads standard VCDIFF patches with the default code table and uncompressed sections. For other xdelta3 features, use these NuGet add-ons.

| Package | Example |
| --- | --- |
| [xdelta.Lzma](https://github.com/israellot/xdeltasharp/blob/master/src/XDeltaSharp.Lzma/README.md) | Read LZMA-compressed patches with `LzmaVcdiff.Decode(original, patch)`. |
| [xdelta.Blake3](https://github.com/israellot/xdeltasharp/blob/master/src/XDeltaSharp.Blake3/README.md) | Verify embedded digests with `Vcdiff.Decode(original, patch, armor: Blake3Armor.Verify)`. |

## Benchmarks against xdelta3

For two editions of *Moby-Dick*, XDeltaSharp encoded the **1.25 MB** update as a **21.3 kB patch (1.7%)**. At the same compression level (3), xdelta3 produced a **21.5 kB** patch. Both decoders applied the xdelta3 patch. [Sample files](https://github.com/israellot/DeltaSharp/tree/main/src/Benchmarks/Data)

Median time per operation in memory, in milliseconds (.NET 8.0.34 and native xdelta3 3.2.2).

| Host | XDeltaSharp encode | xdelta3 encode | XDeltaSharp decode | xdelta3 decode |
| --- | ---: | ---: | ---: | ---: |
| Apple M1 · macOS 15 | 4.71 | 6.54 | 0.25 | 0.29 |
| Intel Xeon 6975P-C · Ubuntu 24.04 | 2.87 | 3.41 | 0.23 | 0.21 |
| AMD EPYC 7763 · Ubuntu 22.04 | 3.91 | 4.10 | 0.27 | 0.23 |

[Full results (CSV)](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/Results/MobyDick-xdelta3-net8.csv) · [Benchmark method](https://github.com/israellot/xdeltasharp/blob/master/benchmarks/README.md#reproduce-the-readme-comparison) · [MIT license](https://opensource.org/license/mit)
