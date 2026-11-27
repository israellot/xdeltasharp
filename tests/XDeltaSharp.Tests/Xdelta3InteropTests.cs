using System.Diagnostics;
using Xunit;

namespace XDeltaSharp.Tests;

public class Xdelta3InteropTests
{
    private sealed class Xdelta3FactAttribute : FactAttribute
    {
        public Xdelta3FactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDELTA3")))
                Skip = "Set XDELTA3 to the xdelta3 executable to run CLI interoperability tests.";
        }
    }

    [Xdelta3Fact]
    [Trait("Category", "Interop")]
    public void Xdelta3AppliesPatchesAtEveryCompressionLevel()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] source = File.ReadAllBytes(Path.Combine(fixtures, "source.txt"));
        byte[] target = [.. File.ReadAllBytes(Path.Combine(fixtures, "target.txt")), .. new byte[128]];
        for (int level = 0; level <= 9; level++)
        {
            foreach (bool optimize in new[] { false, true })
            {
                var options = new VcdiffEncodeOptions { CompressionLevel = level, Optimize = optimize };
                AssertXdelta3Decodes(source, Vcdiff.Encode(source, target, options), target,
                    $"level {level}, optimize {optimize}");
            }
        }
    }

    [Xdelta3Fact]
    [Trait("Category", "Interop")]
    public void Xdelta3AppliesMultipleWindowsAndCopiesBeyond64MiB()
    {
        const int boundary = 64 << 20;
        const int tailLength = 4096;
        byte[] source = new byte[boundary + tailLength];
        new Random(23).NextBytes(source.AsSpan(boundary));
        byte[] target = (byte[])source.Clone();
        target.AsSpan(0, 8 << 20).Fill(0x5A);
        byte[] patch = Vcdiff.Encode(source, target, new VcdiffEncodeOptions { CompressionLevel = 0 });
        // The random tail must be copied from the shifted dictionary for the patch to fit.
        Assert.True(patch.Length < tailLength);
        AssertXdelta3Decodes(source, patch, target, "multi-window source COPY");
    }

    private static void AssertXdelta3Decodes(byte[] source, byte[] patch, byte[] target, string scenario)
    {
        var work = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(work.FullName, "source"), source);
            File.WriteAllBytes(Path.Combine(work.FullName, "patch"), patch);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("XDELTA3")!,
                ArgumentList = { "-d", "-s", "source", "patch", "restored" },
                WorkingDirectory = work.FullName,
                UseShellExecute = false,
                RedirectStandardError = true,
            })!;
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"{scenario}: xdelta3 exited with {process.ExitCode}. {error}");
            Assert.True(target.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(work.FullName, "restored"))),
                $"{scenario}: xdelta3 reconstructed a different target.");
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
