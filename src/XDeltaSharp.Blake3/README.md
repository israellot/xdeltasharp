# xdelta.Blake3

Check that an xdelta3 patch was applied to the expected source and produced the expected target. This .NET 8 add-on verifies the BLAKE3 digests stored in the patch’s application header. NuGet installs the core decoder and managed BLAKE3 implementation as dependencies.

```sh
dotnet add package xdelta.Blake3
```

```csharp
using XDeltaSharp;
using XDeltaSharp.Blake3;

byte[] original = File.ReadAllBytes("original.bin");
byte[] patch = File.ReadAllBytes("update.vcdiff");
byte[] restored = Vcdiff.Decode(original, patch,
    armor: Blake3Armor.Verify, maxOutputBytes: 256 * 1024 * 1024);
```

`Blake3Armor.Verify` checks each recognized digest and accepts patches without armor. `Blake3Armor.Require` requires a target digest. It also requires a source digest if the application header includes source fields or a window declares a source dictionary. A missing required digest raises `InvalidDataException` before the affected window is written. A dictionary-free patch can use a target-only header. Embedded digests detect corruption. To authenticate a patch, verify it against a separately trusted signature or digest.

Both options work with allocating, caller-owned span, and stream output. The core decoder accepts uncompressed sections, including patches made with `xdelta3 -S none`. For an LZMA-compressed patch, also reference `xdelta.Lzma` and pass the same option to `LzmaVcdiff.Decode`.

A wrong source raises `InvalidDataException` before any target bytes are written. A target digest mismatch raises the same exception after reconstruction, when a supplied span or stream can already contain the full output. Source verification on a stream hashes from its initial position to EOF and requires a readable, seekable source. The decoder hashes streamed target windows incrementally. [xdelta3 armor format](https://github.com/jmacd/xdelta/blob/v3.2.2/site/docs/armor.md) · [XDeltaSharp usage](https://www.nuget.org/packages/xdelta#readme-body-tab)
