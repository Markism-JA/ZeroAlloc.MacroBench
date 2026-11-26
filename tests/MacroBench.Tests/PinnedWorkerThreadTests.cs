using MacroBench.Orchestration;

namespace MacroBench.Tests;

public class PinnedWorkerThreadTests
{
    [Fact]
    public void Spawn_BestEffort_RunsAndWakesViaTwoPhaseParker()
    {
        int iterations = 0;
        using var ready = new ManualResetEventSlim(false);
        using var done = new ManualResetEventSlim(false);

        using var worker = PinnedWorkerThread.Spawn(
            "TestWorker",
            coreIndex: 0,
            ctx =>
            {
                ready.Set();
                while (ctx.ShouldContinue())
                {
                    if (ctx.TryArm())
                    {
                        if (!ctx.ShouldContinue())
                        {
                            ctx.Disarm();
                            break;
                        }
                        ctx.Park(timeoutMs: 50);
                    }

                    Interlocked.Increment(ref iterations);
                    if (Volatile.Read(ref iterations) >= 3)
                    {
                        done.Set();
                        break;
                    }
                }
            },
            priority: ThreadPriority.AboveNormal,
            pinningMode: WorkerPinningMode.BestEffort
        );

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        for (int i = 0; i < 3; i++)
        {
            worker.NativeHandle.Unpark();
            Thread.Sleep(5);
        }

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(worker.WaitForCompletion(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Spawn_Unpinned_DoesNotPin()
    {
        bool? observedPinned = null;
        using var done = new ManualResetEventSlim(false);

        using var worker = PinnedWorkerThread.Spawn(
            "UnpinnedWorker",
            coreIndex: 0,
            ctx =>
            {
                observedPinned = ctx.IsPinned;
                done.Set();
            },
            pinningMode: WorkerPinningMode.Unpinned
        );

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(observedPinned);
        Assert.False(worker.IsPinned);
    }

    [Fact]
    public void Spawn_StrictOutOfRangeCore_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PinnedWorkerThread.Spawn(
                "InvalidCoreWorker",
                coreIndex: System.Environment.ProcessorCount + 64,
                _ => { },
                pinningMode: WorkerPinningMode.Strict
            )
        );
    }
}
