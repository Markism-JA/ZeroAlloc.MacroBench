using MacroBench.Pacing;

namespace MacroBench.Tests;

public class OpenLoopPacerTests
{
    [Fact]
    public void WaitNext_Paced_ReturnsScheduledTimestampsAndTracksSlips()
    {
        // 1,000 ops/sec => 1,000,000 ns (1 ms) exact interval
        var pacer = OpenLoopPacer.CreatePaced(1_000);

        long t0 = pacer.WaitNext();
        long t1 = pacer.WaitNext();
        Assert.Equal(1_000_000L, t1 - t0);

        // Simulate producer stalling for 15 ms (coordinated omission scenario)
        Thread.Sleep(15);
        long t2 = pacer.WaitNext();

        // Scheduled timestamp must still advance by exact 1 ms interval, not wall time
        Assert.Equal(1_000_000L, t2 - t1);
        Assert.True(pacer.ScheduleSlips >= 1);
        Assert.Equal(3, pacer.OperationsIssued);
    }

    [Fact]
    public void TryAcquire_Saturated_AlwaysSucceeds()
    {
        var pacer = OpenLoopPacer.CreateSaturated();

        Assert.True(pacer.TryAcquire(out long ts1));
        Assert.True(pacer.TryAcquire(out long ts2));
        Assert.True(ts2 >= ts1);
        Assert.Equal(2, pacer.OperationsIssued);
        Assert.Equal(0, pacer.ScheduleSlips);
    }

    [Fact]
    public void Reset_ClearsCountersAndSlips()
    {
        var pacer = OpenLoopPacer.CreatePaced(10_000);
        pacer.WaitNext();
        Thread.Sleep(5);
        pacer.WaitNext();
        Assert.True(pacer.ScheduleSlips >= 1);

        pacer.Reset();
        Assert.Equal(0, pacer.OperationsIssued);
        Assert.Equal(0, pacer.ScheduleSlips);
    }
}
