using System.Diagnostics;

namespace XDeltaSharp.Benchmarks;

internal static class BenchmarkTimer
{
    internal static (double Nanoseconds, double AllocatedBytes) Measure(Action operation, int warmupMilliseconds)
    {
        int batch = 1;
        while (true)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < batch; i++) operation();
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 15 || batch >= 1 << 20) break;
            batch *= 2;
        }

        long warmup = Stopwatch.GetTimestamp();
        do
        {
            for (int i = 0; i < batch; i++) operation();
        } while (Stopwatch.GetElapsedTime(warmup).TotalMilliseconds < warmupMilliseconds);

        double[] timings = new double[7];
        long allocated = 0;
        for (int i = 0; i < timings.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < batch; j++) operation();
            timings[i] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / batch;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Array.Sort(timings);
        return (timings[timings.Length / 2], (double)allocated / batch / timings.Length);
    }
}
