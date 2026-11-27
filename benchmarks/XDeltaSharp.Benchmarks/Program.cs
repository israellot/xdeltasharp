using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XDeltaSharp;
using XDeltaSharp.Benchmarks;

// Use Release. Report the median of seven batches after warm-up, excluding fixture setup and validation.
Console.Error.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
if (args is ["--decode"])
{
    DecoderBenchmarks.Run();
    return;
}
if (args is ["--compare-xdelta3", var comparisonSource, var comparisonTarget, var cliPatches])
{
    Xdelta3Comparison.Run(File.ReadAllBytes(comparisonSource), File.ReadAllBytes(comparisonTarget), cliPatches);
    return;
}
if (args is ["--profile-default", var sourcePath, var targetPath])
{
    ProfileDefault(File.ReadAllBytes(sourcePath), File.ReadAllBytes(targetPath));
    return;
}
if (args is ["--levels", var levelSource, var levelTarget])
{
    BenchmarkLevels(File.ReadAllBytes(levelSource), File.ReadAllBytes(levelTarget));
    return;
}
const string encoderHeader = "Case,Level,Memory mode,Source B,Target B,Patch B,API,ns/op,Allocated B/op";
if (args is ["--short-search"])
{
    byte[] source = Bytes(64 << 20);
    byte[] unrelated = new byte[256];
    new Random(99).NextBytes(unrelated);
    Console.WriteLine(encoderHeader);
    MeasureEncoder("unrelated short target", source, unrelated, 3, MemoryMode.Default);
    MeasureEncoder("distant short match", source, source[^256..], 3, MemoryMode.Default);
    return;
}
if (args is ["--afterburner", var originalFile, var targetFile])
{
    var source = File.ReadAllBytes(originalFile);
    var target = File.ReadAllBytes(targetFile);
    Console.WriteLine(encoderHeader);
    for (int level = 0; level <= 9; level++)
    {
        MeasureEncoder("ordinary", source, target, level, MemoryMode.Default);
        MeasureEncoder("afterburner", source, target, level, MemoryMode.Default, optimize: true);
    }
    return;
}
if (args.Length != 0 && (args.Length != 2 || args[0].StartsWith("--", StringComparison.Ordinal)))
    throw new ArgumentException("Pass a source and target pair, --decode, --short-search, --compare-xdelta3 <source> <target> <cli-patch-dir>, or --levels, --afterburner, or --profile-default followed by the pair.");

Console.WriteLine(encoderHeader);
foreach (int size in new[] { 0, 4, 8, 16, 32, 64, 256, 2047, 2048, 524287, 524288 })
{
    byte[] source = Bytes(size);
    byte[] target = (byte[])source.Clone();
    if (size > 1) target[size / 2] ^= 1;
    Benchmark("one edit", source, target);
}

Benchmark("tiny target", Bytes(1 << 20), "tiny target"u8.ToArray());
Benchmark("short target", Bytes(1 << 20), Bytes(64));
Benchmark("tiny source", Bytes(32), Bytes(1 << 20));
Benchmark("source-free runs", [], new byte[1 << 20]);
Benchmark("source-free ABCD", [], Enumerable.Repeat("ABCD"u8.ToArray(), 16384).SelectMany(x => x).ToArray());
byte[] interrupted = new byte[1 << 20];
for (int i = 0; i < interrupted.Length; i++) interrupted[i] = (byte)(i % 4);
interrupted[interrupted.Length / 2] = 42;
Benchmark("interrupted period", [], interrupted);
Benchmark("two windows", [], new byte[(8 << 20) + 1]);
// Reverse distinct 32-byte records that all start with the same indexed landmark.
byte[] records = Bytes(256 * 32);
byte[] reversed = new byte[records.Length];
for (int offset = 0; offset < records.Length; offset += 32)
{
    "landmark"u8.CopyTo(records.AsSpan(offset));
    records.AsSpan(offset, 32).CopyTo(reversed.AsSpan(records.Length - offset - 32));
}
Benchmark("reordered duplicate seeds", records, reversed);
if (args is [var originalPath, var updatedPath])
    Benchmark("file pair", File.ReadAllBytes(originalPath), File.ReadAllBytes(updatedPath));

static byte[] Bytes(int length)
{
    var bytes = new byte[length];
    new Random(731).NextBytes(bytes);
    return bytes;
}

static void BenchmarkLevels(byte[] source, byte[] target)
{
    Console.WriteLine("Level,Patch bytes,Median ms");
    for (int level = 0; level <= 9; level++)
    {
        byte[] expected = Vcdiff.Encode(source, target, level);
        if (!Vcdiff.Decode(source, expected).AsSpan().SequenceEqual(target))
            throw new InvalidDataException("Level " + level + " failed to reconstruct target.");
        var writer = new ArrayBufferWriter<byte>(expected.Length);
        void Encode()
        {
            writer.ResetWrittenCount();
            Vcdiff.Encode(source, target, writer, level);
        }
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 600) Encode();
        var times = new double[9];
        for (int batch = 0; batch < times.Length; batch++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 12; i++) Encode();
            times[batch] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 12;
        }
        if (!expected.AsSpan().SequenceEqual(writer.WrittenSpan)) throw new InvalidDataException("Writer output differs.");
        Array.Sort(times);
        Console.WriteLine(FormattableString.Invariant($"{level},{expected.Length},{times[4]:F3}"));
    }
}

static void ProfileDefault(byte[] source, byte[] target)
{
    byte[] expected = Vcdiff.Encode(source, target);
    if (!Vcdiff.Decode(source, expected).AsSpan().SequenceEqual(target))
        throw new InvalidDataException("Round trip failed.");
    var writer = new ArrayBufferWriter<byte>(expected.Length);
    void Encode()
    {
        writer.ResetWrittenCount();
        Vcdiff.Encode(source, target, writer);
    }

    var warmup = Stopwatch.StartNew();
    while (warmup.ElapsedMilliseconds < 1800) Encode();
    double[] times = new double[32];
    for (int batch = 0; batch < times.Length; batch++)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 12; i++) Encode();
        times[batch] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 12;
    }
    if (!writer.WrittenSpan.SequenceEqual(expected)) throw new InvalidDataException("Writer output differs.");
    Array.Sort(times);
    Console.WriteLine($"{expected.Length} bytes, SHA-256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected))}");
    Console.WriteLine($"Median {(times[15] + times[16]) / 2:F3} ms/encode");
}

static void Benchmark(string name, byte[] source, byte[] target)
{
    MeasureEncoder(name, source, target, 3, MemoryMode.Default);
    MeasureEncoder(name, source, target, 3, MemoryMode.LessMemory);
}

static void MeasureEncoder(string name, byte[] source, byte[] target, int level, MemoryMode memoryMode, bool optimize = false)
{
    byte[] EncodeArray() => optimize ? Vcdiff.EncodeOptimized(source, target, level, memoryMode) :
        Vcdiff.Encode(source, target, level, memoryMode);
    byte[] reference = EncodeArray();
    if (!Vcdiff.Decode(source, reference).AsSpan().SequenceEqual(target))
        throw new InvalidDataException("Round trip failed.");
    var writer = new ArrayBufferWriter<byte>(reference.Length);
    void EncodeInto()
    {
        writer.ResetWrittenCount();
        if (optimize) Vcdiff.EncodeOptimized(source, target, writer, level, memoryMode);
        else Vcdiff.Encode(source, target, writer, level, memoryMode);
    }

    EncodeInto();
    if (!writer.WrittenSpan.SequenceEqual(reference)) throw new InvalidDataException("Writer output differs.");
    Measure("writer", EncodeInto);
    Measure("array", () => EncodeArray());

    void Measure(string api, Action encode)
    {
        var (nanoseconds, allocated) = BenchmarkTimer.Measure(encode, warmupMilliseconds: 150);
        Console.WriteLine(FormattableString.Invariant(
            $"{name},{level},{memoryMode},{source.Length},{target.Length},{reference.Length},{api},{nanoseconds:F1},{allocated:F1}"));
    }
}
