# Independent decoder fixtures

The single-window fixtures reconstruct `target.txt` from `source.txt`. The files use LF endings. The tests compare the target bytes across the allocating, span, and stream decoders.

The two uncompressed xdelta3 fixtures were generated with xdelta3 3.2.2. The second includes the default application header and a target-window Adler-32 checksum of `92F15237`.

```sh
xdelta3 -e -9 -S none -A -a -n -s source.txt target.txt xdelta3.vcdiff
xdelta3 -e -S none -s source.txt target.txt xdelta3-checksum.vcdiff
```

The open-vcdiff fixture was generated with Google open-vcdiff revision `868f459a8d815125c2457f8c74b12493853100f9`, using `VCDiffStreamingEncoder` with `VCD_STANDARD_FORMAT`, target matching enabled, and the target supplied in one chunk. The equivalent CLI command is:

```sh
vcdiff encode -dictionary source.txt -target target.txt -target_matches -delta open-vcdiff.vcdiff
```

## Default xdelta3 multi-window fixture

`xdelta3-lzma.vcdiff` was generated with the LZMA-enabled xdelta3 3.2.2 CLI using default compression, window size, checksums, and armor. To regenerate it, run the following in a scratch directory containing the two text fixtures.

```sh
python3 - <<'PY'
from pathlib import Path
source = Path("source.txt").read_bytes()
sample = Path("target.txt").read_bytes()
prefix = bytearray(sample)
for i in range(48):
    start = i * 7 % 110
    prefix.extend(source[start:start + 32])
    prefix.append(65 + i)
window = 8 * 1024 * 1024
Path("multi.bin").write_bytes(prefix.ljust(window, b"\x00") + sample.ljust(window, b"\xff") + prefix + b"!")
PY
xdelta3 -e -s source.txt multi.bin xdelta3-lzma.vcdiff
xdelta3 printhdrs xdelta3-lzma.vcdiff
```

| Window | Target bytes | Data | Instructions | Addresses |
| --- | ---: | --- | --- | --- |
| 0 | 8,388,608 | LZMA | LZMA | LZMA |
| 1 | 8,388,608 | LZMA | LZMA | Raw |
| 2 | 1,811 | LZMA | LZMA | LZMA |
