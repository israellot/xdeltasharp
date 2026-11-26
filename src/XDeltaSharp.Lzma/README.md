# xdelta.Lzma

Apply an xdelta3 patch with LZMA secondary compression in .NET 8. `LzmaVcdiff` also accepts uncompressed VCDIFF patches and handles a mix of raw and compressed sections across windows. The package includes the core `xdelta` dependency and uses SharpCompress for managed LZMA2 decoding.

```sh
dotnet add package xdelta.Lzma
```

```csharp
using XDeltaSharp.Lzma;

using var original = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");
long written = LzmaVcdiff.Decode(original, patch, restored, maxOutputBytes: 512L * 1024 * 1024);
```

For in-memory inputs, `LzmaVcdiff.Decode(source, patch)` returns a new target array. Pass `destination.AsSpan()` as the third argument to fill an existing buffer and receive the byte count.

All overloads verify any Adler-32 window checksums. Each decoded target window, LZMA dictionary, and expanded section is limited to 64 MiB. A stream target can span many windows. Use `maxOutputBytes` to cap the entire output and `maxWindowBytes` to tighten the decoded target window limit.

To verify xdelta3 BLAKE3 armor as well, reference `xdelta.Blake3` and pass `armor: Blake3Armor.Verify` from the `XDeltaSharp.Blake3` namespace. To create a patch, use the core `XDeltaSharp.Vcdiff.Encode` API, which writes uncompressed sections. [Stream requirements and compatibility](https://github.com/israellot/xdeltasharp#buffers-streams-and-limits)
