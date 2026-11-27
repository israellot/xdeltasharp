using System.Buffers;
using System.Runtime.InteropServices;

namespace XDeltaSharp.Benchmarks;

internal static class Xdelta3Comparison
{
    internal static void Run(byte[] source, byte[] target, string cliPatches)
    {
        Console.WriteLine("Operation,Level,Patch encoder,Library,Patch bytes,Median ms");
        byte[] nativeBuffer = new byte[checked(target.Length * 2 + 1024)];
        byte[] restored = new byte[target.Length];
        for (int level = 0; level <= 9; level++)
        {
            var options = new VcdiffEncodeOptions { CompressionLevel = level };
            byte[] expected = Vcdiff.Encode(source, target, options);
            var writer = new ArrayBufferWriter<byte>(expected.Length);
            void EncodeManaged()
            {
                writer.ResetWrittenCount();
                Vcdiff.Encode(source, target, writer, options);
            }

            int patchLength = 0;
            void EncodeNative()
            {
                CheckStatus(NativeEncode(source, (nuint)source.Length, target, (nuint)target.Length,
                    nativeBuffer, (nuint)nativeBuffer.Length, out nuint length, level));
                patchLength = checked((int)length);
            }

            double managedMs = Measure(EncodeManaged);
            if (!writer.WrittenSpan.SequenceEqual(expected))
                throw new InvalidDataException("Managed writer output differs from the array API.");
            double nativeMs = Measure(EncodeNative);
            byte[] nativePatch = nativeBuffer[..patchLength];
            if (!nativePatch.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(cliPatches, $"{level}.vcdiff"))))
                throw new InvalidDataException($"Native level-{level} output differs from the xdelta3 CLI.");
            Report("encode", level, "XDeltaSharp", "XDeltaSharp", expected.Length, managedMs);
            Report("encode", level, "xdelta3", "xdelta3", nativePatch.Length, nativeMs);
            CompareDecoders("XDeltaSharp", expected);
            CompareDecoders("xdelta3", nativePatch);

            void CompareDecoders(string encoder, byte[] patch)
            {
                int written = 0;
                void DecodeManaged() => written = Vcdiff.Decode(source, patch, restored.AsSpan());
                void DecodeNative()
                {
                    CheckStatus(NativeDecode(source, (nuint)source.Length, patch, (nuint)patch.Length,
                        restored, (nuint)restored.Length, out nuint length));
                    written = checked((int)length);
                }

                foreach (var (library, decode) in new (string, Action)[] { ("XDeltaSharp", DecodeManaged), ("xdelta3", DecodeNative) })
                {
                    Array.Fill(restored, (byte)0xFF);
                    if (level == 3)
                        Report("decode", level, encoder, library, patch.Length, Measure(decode));
                    else
                        decode();
                    if (written != target.Length || !restored.AsSpan().SequenceEqual(target))
                        throw new InvalidDataException($"{library} failed to restore the {encoder} level-{level} patch.");
                }
            }
        }
    }

    private static double Measure(Action operation) =>
        BenchmarkTimer.Measure(operation, warmupMilliseconds: 600).Nanoseconds / 1_000_000;

    private static void Report(string operation, int level, string encoder, string library, int bytes, double ms) =>
        Console.WriteLine(FormattableString.Invariant($"{operation},{level},{encoder},{library},{bytes},{ms:F6}"));

    private static void CheckStatus(int status)
    {
        if (status != 0) throw new InvalidDataException($"xdelta3 returned {status}.");
    }

    [DllImport("xdelta_benchmark", EntryPoint = "benchmark_encode")]
    private static extern int NativeEncode(byte[] source, nuint sourceLength, byte[] target, nuint targetLength,
        [Out] byte[] output, nuint capacity, out nuint written, int level);

    [DllImport("xdelta_benchmark", EntryPoint = "benchmark_decode")]
    private static extern int NativeDecode(byte[] source, nuint sourceLength, byte[] patch, nuint patchLength,
        [Out] byte[] output, nuint capacity, out nuint written);
}
