# Contributing to XDeltaSharp

## Build and test

Run these commands from the repository root with a .NET SDK that supports C# 12 and .NET 8.

```sh
dotnet test XDeltaSharp.sln -c Release
dotnet format whitespace XDeltaSharp.sln --verify-no-changes
python3 eng/test-packages.py
```

The package smoke test runs the README examples against a fresh local feed. Release CI uses the SDK in `.github/global.json`. Record intentional public API additions in the library’s `PublicAPI.Unshipped.txt`. See the [benchmark guide](benchmarks/README.md) for encoder and decoder measurements.

## Publish a release

Set `VersionPrefix` in `src/Directory.Build.props` before tagging the release commit. All three packages share that version. A manual run of **Publish NuGet packages** builds and tests the artifacts for inspection. Pushing a matching tag such as `v1.0.0` publishes the verified packages and symbols to NuGet.org.

The trusted publishing policy binds `israellot/xdeltasharp`, workflow `publish.yml`, and environment `nuget` to the package IDs `xdelta`, `xdelta.Lzma`, and `xdelta.Blake3`. [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
