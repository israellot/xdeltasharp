# VCDiffSharp

A .NET library for creating and applying VCDIFF patches. This repository contains the proposed API and project structure. The codec methods currently throw `NotImplementedException`.

## Public API

```csharp
using VCDiffSharp;

byte[] patch = Vcdiff.Encode(source, target);
byte[] restored = Vcdiff.Decode(source, patch, maxOutputBytes: target.Length);
```

Callers can also encode into an `IBufferWriter<byte>` or decode into a `Span<byte>`. The span overload returns the number of target bytes written.

## Structure

| Type | Responsibility |
| --- | --- |
| `Vcdiff` | Public entry points, file header, window sequence, and total output limit. |
| `Encoding/WindowEncoder` | Select ADD, RUN, and COPY operations using source data and earlier bytes in the current window. Emit the framed window. |
| `Decoding/WindowDecoder` | Execute the data, instruction, and address sections against the selected dictionary and the target window being reconstructed. |
| `Format/AddressCache` | Maintain the per-window NEAR and SAME caches shared by the encoding and decoding algorithms. |
| `Format/DefaultCodeTable` | Describe the single and paired instructions represented by each of the 256 default opcodes. |

The initial design targets [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284.html) version 0, the default code table, and uncompressed sections. The test project is reserved for format vectors and interoperability tests.
