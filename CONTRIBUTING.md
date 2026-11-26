# Contributing to XDeltaSharp

## Set up a branch

Start from the latest `master` and create a branch for your change. If you’re contributing from a fork, sync it with `israellot/xdeltasharp` first. From the repository root, use the .NET 8 SDK (8.0.400 or later in the 8.0 series) to run the tests.

```sh
git switch -c fix-window-decoding
dotnet test XDeltaSharp.sln -c Release
```

## Make and test your change

The core codec lives in `src/XDeltaSharp`. The optional LZMA and BLAKE3 implementations have their own projects under `src/`. The test suite for all three is in `tests/XDeltaSharp.Tests`.

- **Bug fixes.** Add a test that reproduces the problem. For a wire-format issue, a small source, patch, and expected target make the change easier to review. The [fixture guide](tests/XDeltaSharp.Tests/Fixtures/README.md) explains how the existing xdelta3 and open-vcdiff fixtures were generated.
- **Public API changes.** Update the XML documentation and record new public signatures in the affected project’s `PublicAPI.Unshipped.txt`. If you change a README example, run `python3 eng/test-packages.py` to exercise it as a package consumer.
- **Performance changes.** Compare the same workload before and after your change. Include the input, compression level, runtime, and architecture with your results. The [benchmark guide](benchmarks/README.md) lists the commands.

Run `dotnet test XDeltaSharp.sln -c Release` and `dotnet format whitespace XDeltaSharp.sln --verify-no-changes` before submitting.

## Open a pull request

Push your branch and open a pull request against `israellot/xdeltasharp:master`. Describe the behavior you changed, why it matters to a caller, and how you tested it. Link the issue if there is one. GitHub Actions runs the suite on Linux and Windows. Check the results and push any follow-up fixes to the same branch.
