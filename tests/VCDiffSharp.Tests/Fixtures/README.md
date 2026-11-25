# Independent decoder fixtures

Both patches reconstruct `target.txt` from `source.txt`. The files use LF endings. The tests compare the original target bytes against both public decoder overloads.

The xdelta3 fixture was generated with xdelta3 3.2.2:

```sh
xdelta3 -e -9 -S none -A -a -n -s source.txt target.txt xdelta3.vcdiff
```

The open-vcdiff fixture was generated with Google open-vcdiff revision `868f459a8d815125c2457f8c74b12493853100f9`, using `VCDiffStreamingEncoder` with `VCD_STANDARD_FORMAT`, target matching enabled, and the target supplied in one chunk. The equivalent CLI command is:

```sh
vcdiff encode -dictionary source.txt -target target.txt -target_matches -delta open-vcdiff.vcdiff
```
