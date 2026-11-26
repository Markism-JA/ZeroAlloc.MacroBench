using MacroBench.Telemetry;

namespace MacroBench.Tests;

public unsafe class NativeHistogramTests
{
    private static void NativeRecordCallback(NativeHistogramHandle handle, long valueNs)
    {
        handle.Record(valueNs);
    }

    [Fact]
    public void RecordAndSnapshot_ExactSub256AndAccurateHigherOctaves()
    {
        using var hist = new NativeHistogram();

        for (int i = 1; i <= 1000; i++)
        {
            hist.Record(i * 1000L); // 1 µs .. 1000 µs
        }

        HistogramSnapshot snap = hist.Snapshot();
        Assert.Equal(1000, snap.TotalCount);
        Assert.Equal(1_000, snap.MinNs);
        Assert.Equal(1_000_000, snap.MaxNs);
        Assert.InRange(snap.P50Ns, 495_000, 510_000);
        Assert.InRange(snap.P90Ns, 895_000, 910_000);
        Assert.InRange(snap.P99Ns, 985_000, 1_000_000);
        Assert.InRange(snap.P999Ns, 995_000, 1_000_000);
    }

    [Fact]
    public void BlittableHandle_WorksFromFunctionPointerWithoutGcHandle()
    {
        using var hist = new NativeHistogram();
        delegate*<NativeHistogramHandle, long, void> fn = &NativeRecordCallback;

        fn(hist.Handle, 42);
        fn(hist.Handle, 255);
        fn(hist.Handle, 256);

        HistogramSnapshot snap = hist.Snapshot();
        Assert.Equal(3, snap.TotalCount);
        Assert.Equal(42, snap.MinNs);
        Assert.Equal(256, snap.MaxNs);
        Assert.Equal(255, snap.P50Ns);
    }

    [Fact]
    public void MergeFrom_CombinesPerWorkerHistograms()
    {
        using var w1 = new NativeHistogram();
        using var w2 = new NativeHistogram();
        using var merged = new NativeHistogram();

        w1.Record(100);
        w1.Record(200);
        w2.Record(50);
        w2.Record(5_000_000);

        merged.MergeFrom(w1);
        merged.MergeFrom(w2);

        HistogramSnapshot snap = merged.Snapshot();
        Assert.Equal(4, snap.TotalCount);
        Assert.Equal(50, snap.MinNs);
        Assert.Equal(5_000_000, snap.MaxNs);
    }

    [Fact]
    public void RecordAtomic_MultiThread_PreservesTotalCount()
    {
        using var hist = new NativeHistogram();
        const int threads = 4;
        const int perThread = 10_000;

        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++)
            {
                hist.RecordAtomic((t + 1) * 100L + (i % 50));
            }
        });

        Assert.Equal(threads * perThread, hist.TotalCount);
    }

    [Fact]
    public void UnmanagedLinearHistogram_SinglePassSnapshot_MatchesExpected()
    {
        using var hist = new UnmanagedLinearHistogram(maxTrackedNs: 10_000);
        for (int i = 1; i <= 100; i++)
        {
            hist.Record(i * 10);
        }

        HistogramSnapshot snap = hist.Snapshot();
        Assert.Equal(100, snap.TotalCount);
        Assert.Equal(10, snap.MinNs);
        Assert.Equal(1000, snap.MaxNs);
        Assert.Equal(500, snap.P50Ns);
        Assert.Equal(900, snap.P90Ns);
        Assert.Equal(990, snap.P99Ns);
    }
}
