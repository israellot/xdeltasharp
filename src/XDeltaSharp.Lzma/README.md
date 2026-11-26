# xdelta.Lzma

Apply an xdelta3 patch with LZMA secondary compression in .NET 8. `LzmaVcdiff` also accepts uncompressed VCDIFF patches and handles a mix of raw and compressed sections across windows. The package includes the core `xdelta` dependency and uses SharpCompress for managed LZMA2 decoding.

```sh
dotnet add package xdelta.Lzma
```

The following example uses the secondary limits added after 1.0.0.

```csharp
using XDeltaSharp.Lzma;

using var original = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");
long written = LzmaVcdiff.DecodeWithSecondaryLimits(original, patch, restored,
    maxEncodedWindowBytes: 8 * 1024 * 1024,
    maxExpandedSectionBytes: 8 * 1024 * 1024,
    maxDictionaryBytes: 16 * 1024 * 1024,
    maxOutputBytes: 512L * 1024 * 1024);
```

For in-memory inputs, `LzmaVcdiff.Decode(source, patch)` returns a new target array. Pass `destination.AsSpan()` as the third argument to fill an existing buffer and receive the byte count.

All overloads verify any Adler-32 window checksums. Use `maxOutputBytes` to cap the entire output and `maxWindowBytes` to tighten the default 64 MiB target window limit. The stream example also bounds the combined encoded payload before reading each window.

LZMA uses separate streams for data, instructions, and addresses. By default each stream can expand a section to 64 MiB and retain a dictionary of up to 64 MiB across windows. `DecodeWithSecondaryLimits` lets array, span, and stream callers lower both limits. The example permits up to 24 MiB of expanded section contents and three declared dictionaries of up to 16 MiB each, in addition to the target, encoded input, and codec overhead. The decoder checks the declared dictionary size before starting each stream.

To verify xdelta3 BLAKE3 armor as well, reference `xdelta.Blake3` and pass `armor: Blake3Armor.Verify` from the `XDeltaSharp.Blake3` namespace. To create a patch, use the core `XDeltaSharp.Vcdiff.Encode` API, which writes uncompressed sections. [Stream requirements and compatibility](https://www.nuget.org/packages/xdelta#readme-body-tab)
