using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VCDiffSharp;

// Use Release. Report the median of seven batches after warm-up, excluding fixture setup and validation.
Console.Error.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
if (args is ["--profile-default", var sourcePath, var targetPath])
{
    ProfileDefault(File.ReadAllBytes(sourcePath), File.ReadAllBytes(targetPath));
    return;
}
Console.WriteLine("Case,Search mode,Memory mode,Source B,Target B,Patch B,API,ns/op,Allocated B/op");

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
if (args.Length == 2)
    Benchmark("file pair", File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]));
else if (args.Length != 0)
    throw new ArgumentException("Pass source and target paths together.");

static byte[] Bytes(int length)
{
    var bytes = new byte[length];
    new Random(731).NextBytes(bytes);
    return bytes;
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
    foreach (var mode in new[] { VcdiffEncodeMode.Fast, VcdiffEncodeMode.Default })
    {
        MeasureMode(name, source, target, mode, MemoryMode.Default);
        MeasureMode(name, source, target, mode, MemoryMode.LessMemory);
    }
}

static void MeasureMode(string name, byte[] source, byte[] target, VcdiffEncodeMode mode, MemoryMode memoryMode)
{
    byte[] reference = Vcdiff.Encode(source, target, mode, memoryMode);
    if (!Vcdiff.Decode(source, reference).AsSpan().SequenceEqual(target))
        throw new InvalidDataException("Round trip failed.");
    var writer = new ArrayBufferWriter<byte>(reference.Length);
    void EncodeInto()
    {
        writer.ResetWrittenCount();
        Vcdiff.Encode(source, target, writer, mode, memoryMode);
    }

    EncodeInto();
    if (!writer.WrittenSpan.SequenceEqual(reference)) throw new InvalidDataException("Writer output differs.");
    Measure("writer", EncodeInto);
    Measure("array", () => Vcdiff.Encode(source, target, mode, memoryMode));

    void Measure(string api, Action encode)
    {
        int batch = 1;
        while (true)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < batch; i++) encode();
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 15 || batch >= 1 << 20) break;
            batch *= 2;
        }
        long warmup = Stopwatch.GetTimestamp();
        do
        {
            for (int j = 0; j < batch; j++) encode();
        } while (Stopwatch.GetElapsedTime(warmup).TotalMilliseconds < 150);
        double[] timings = new double[7];
        long allocated = 0;
        for (int i = 0; i < timings.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < batch; j++) encode();
            timings[i] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / batch;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(timings);
        Console.WriteLine(FormattableString.Invariant(
            $"{name},{mode},{memoryMode},{source.Length},{target.Length},{reference.Length},{api},{timings[3]:F1},{(double)allocated / batch / timings.Length:F1}"));
    }
}
