"""Pack the solution and run the published README examples in a fresh NuGet consumer."""

import argparse
import hashlib
import re
import shutil
import subprocess
import tempfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import quoteattr
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/XDeltaSharp.Tests/Fixtures"
FRAMEWORKS = ("net8.0", "net9.0", "net10.0", "net11.0")
# multi.bin from the independent recipe in Fixtures/README.md.
MULTI_SHA256 = "bdf1101ea6b7190ebc217cd0782b82320298238fe59e2d0c1b49f38b8f9cde73"
ASSEMBLIES = {
    "xdelta": "XDeltaSharp",
    "xdelta.Blake3": "XDeltaSharp.Blake3",
    "xdelta.Lzma": "XDeltaSharp.Lzma",
}


def check_packages(feed, version):
    expected = {f"{name}.{version}.{ext}" for name in ASSEMBLIES for ext in ("nupkg", "snupkg")}
    actual = {path.name for path in feed.iterdir()}
    if expected != actual:
        raise ValueError(f"Unexpected package set: missing {expected - actual}, extra {actual - expected}")

    for name, assembly in ASSEMBLIES.items():
        with ZipFile(feed / f"{name}.{version}.nupkg") as package:
            manifest = ElementTree.fromstring(package.read(f"{name}.nuspec"))
            if manifest.findtext("{*}metadata/{*}icon") != "icon.png":
                raise ValueError(f"{name} does not declare icon.png as its package icon")
            files = {"README.md", "LICENSE", "icon.png"}
            for framework in FRAMEWORKS:
                files.update((f"lib/{framework}/{assembly}.dll", f"lib/{framework}/{assembly}.xml"))
            if name == "xdelta":
                files.add("THIRD-PARTY-NOTICES.txt")
            for member in files:
                package.getinfo(member)
        with ZipFile(feed / f"{name}.{version}.snupkg") as symbols:
            for framework in FRAMEWORKS:
                symbols.getinfo(f"lib/{framework}/{assembly}.pdb")


def run_example(work, code, packages, version, framework, replacement_core=None):
    references = "".join(f'<PackageReference Include="{name}" Version="[{version}]" />' for name in packages)
    project = work / "Example.csproj"
    project.write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{framework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>{references}</ItemGroup>
</Project>
""")
    (work / "Program.cs").write_text(code)
    subprocess.run(["dotnet", "build", project, "-c", "Release",
                    f"-p:RestorePackagesPath={work.parent / 'cache'}"], cwd=ROOT, check=True)
    output = work / "bin/Release" / framework
    if replacement_core is not None:
        with ZipFile(replacement_core) as package:
            (output / "XDeltaSharp.dll").write_bytes(package.read(f"lib/{framework}/XDeltaSharp.dll"))
    return subprocess.check_output(["dotnet", output / "Example.dll"], cwd=work, text=True)


def check_published_addons(core_package):
    # Both 1.0.0 add-ons call core internals, and their NuGet constraints allow a newer core.
    # Build against the published binaries, then run that same consumer with the candidate core.
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        work = root / "consumer"
        work.mkdir()
        (root / "NuGet.Config").write_text("""<configuration>
  <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
</configuration>""")
        shutil.copy(FIXTURES / "source.txt", work)
        shutil.copy(FIXTURES / "xdelta3-lzma.vcdiff", work)
        code = """using System.Security.Cryptography;
using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;

byte[] source = File.ReadAllBytes("source.txt"), patch = File.ReadAllBytes("xdelta3-lzma.vcdiff");
var armor = Blake3Armor.Verify;
byte[] target = LzmaVcdiff.Decode(source, patch, armor);
byte[] buffer = new byte[target.Length];
int length = LzmaVcdiff.Decode(source, patch, buffer, armor);
using var output = new MemoryStream();
long written = LzmaVcdiff.Decode(new MemoryStream(source), new MemoryStream(patch), output, armor);
if (length != target.Length || written != target.Length || !target.AsSpan().SequenceEqual(buffer)
    || !target.AsSpan().SequenceEqual(output.ToArray())) throw new InvalidDataException();
Console.WriteLine(Convert.ToHexString(SHA256.HashData(target)));
"""
        digest = run_example(work, code, ["xdelta.Lzma", "xdelta.Blake3"], "1.0.0", "net8.0", core_package)
        if digest.strip().lower() != MULTI_SHA256:
            raise ValueError("The published 1.0.0 add-ons reconstructed the wrong target with the new core")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", nargs=2, metavar=("DIRECTORY", "VERSION"),
                        help="test packages already built for release")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        if args.verify:
            directory, version = args.verify
            feed = Path(directory).resolve()
        else:
            version = "1.0.0-localtest"
            feed = root / "feed"
            subprocess.run(["dotnet", "pack", "XDeltaSharp.sln", "-c", "Release",
                            f"-p:Version={version}", "-o", feed], cwd=ROOT, check=True)
        check_packages(feed, version)

        # Use the freshly packed libraries even if this version already exists in the user's cache.
        (root / "NuGet.Config").write_text(f"""<configuration>
  <packageSources>
    <clear />
    <add key="local" value={quoteattr(str(feed))} />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="xdelta" /><package pattern="xdelta.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
""")
        with ZipFile(feed / f"xdelta.{version}.nupkg") as package:
            readme = package.read("README.md").decode()
        round_trip, apply_file = re.findall(r"^```csharp\n(.*?)^```$", readme, re.MULTILINE | re.DOTALL)
        round_trip += '\nif (!restored.AsSpan().SequenceEqual(updated)) throw new InvalidDataException("Round trip failed.");'
        for framework in FRAMEWORKS:
            work = root / framework
            work.mkdir()
            shutil.copy(FIXTURES / "source.txt", work / "original.bin")
            shutil.copy(FIXTURES / "target.txt", work / "updated.bin")
            run_example(work, round_trip, ["xdelta"], version, framework)
            shutil.copy(FIXTURES / "xdelta3-checksum.vcdiff", work / "update.vcdiff")
            run_example(work, apply_file, ["xdelta.Lzma", "xdelta.Blake3"], version, framework)
            if (work / "restored.bin").read_bytes() != (FIXTURES / "target.txt").read_bytes():
                raise ValueError(f"The {framework} packaged file example reconstructed the wrong target")
        check_published_addons(feed / f"xdelta.{version}.nupkg")


if __name__ == "__main__":
    main()
