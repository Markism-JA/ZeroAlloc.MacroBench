using System.Runtime.InteropServices;
using MacroBench.Telemetry;

namespace MacroBench.Tests;

public unsafe class SeqlockMetricsReaderTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TestMetricBlock : ISeqlockMetricBlock
    {
        public uint Sequence { get; set; }
        public long A;
        public long B;
        public long Checksum;
    }

    [Fact]
    public void TryRead_NullPointer_ReturnsFalse()
    {
        Assert.False(SeqlockMetricsReader.TryRead<TestMetricBlock>(null, out _));
    }

    [Fact]
    public void WriteAndTryRead_ConcurrentStress_NeverObservesTornPayload()
    {
        var block = (TestMetricBlock*)NativeMemory.AlignedAlloc((nuint)sizeof(TestMetricBlock), 64);
        NativeMemory.Clear(block, (nuint)sizeof(TestMetricBlock));

        try
        {
            using var cts = new CancellationTokenSource();
            Exception? readerFailure = null;
            int successfulReads = 0;

            var writer = new Thread(() =>
            {
                long i = 1;
                while (!cts.IsCancellationRequested)
                {
                    var sample = new TestMetricBlock
                    {
                        A = i,
                        B = i * 3,
                        Checksum = i + (i * 3),
                    };
                    SeqlockMetricsReader.Write(block, in sample);
                    i++;
                }
            });

            var reader = new Thread(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        if (SeqlockMetricsReader.TryRead(block, out TestMetricBlock read))
                        {
                            Assert.Equal(read.A * 3, read.B);
                            Assert.Equal(read.A + read.B, read.Checksum);
                            Interlocked.Increment(ref successfulReads);
                        }
                    }
                }
                catch (Exception ex)
                {
                    readerFailure = ex;
                }
            });

            writer.Start();
            reader.Start();
            Thread.Sleep(250);
            cts.Cancel();

            Assert.True(writer.Join(TimeSpan.FromSeconds(5)));
            Assert.True(reader.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(readerFailure);
            Assert.True(successfulReads > 100);
        }
        finally
        {
            NativeMemory.AlignedFree(block);
        }
    }
}
